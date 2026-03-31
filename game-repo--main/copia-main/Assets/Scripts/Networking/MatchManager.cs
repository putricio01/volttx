#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side match lifecycle manager.
///
/// States:
///   WaitingForPlayers -> WaitingForReady -> InProgress -> Finished
///
/// Manages one match at a time per server instance.
/// After a match finishes, the server re-registers as idle in the pool.
/// </summary>
public class MatchManager : MonoBehaviour
{
    public enum MatchState
    {
        WaitingForPlayers,
        WaitingForReady,
        InProgress,
        Finished
    }

    public static MatchManager Instance { get; private set; }

    public MatchState CurrentState { get; private set; } = MatchState.WaitingForPlayers;

    const int REQUIRED_PLAYERS = 2;

    // Player tracking
    readonly Dictionary<ulong, PlayerLobbyNet> _players = new();
    readonly HashSet<ulong> _readyPlayers = new();
    string _gamePda;
    bool _metadataMismatchDetected;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Called by PlayerLobbyNet.SubmitPlayerInfoServerRpc when a client submits their info.
    /// </summary>
    public void OnPlayerInfoReceived(ulong clientId, PlayerLobbyNet lobbyNet)
    {
        if (CurrentState == MatchState.InProgress || CurrentState == MatchState.Finished)
        {
            Debug.LogWarning($"[MatchManager] Ignoring late player info from {clientId} - match state: {CurrentState}");
            return;
        }

        if (lobbyNet == null)
        {
            Debug.LogWarning($"[MatchManager] Ignoring null lobbyNet for client {clientId}.");
            return;
        }

        string walletPubkey = lobbyNet.WalletPubkey?.Trim() ?? string.Empty;
        string gamePda = lobbyNet.GamePda?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(walletPubkey) || string.IsNullOrEmpty(gamePda))
        {
            Debug.LogWarning($"[MatchManager] Ignoring incomplete player info from {clientId}.");
            return;
        }

        if (_players.Count >= REQUIRED_PLAYERS && !_players.ContainsKey(clientId))
        {
            Debug.LogWarning($"[MatchManager] Rejecting extra player {clientId}: match already has {REQUIRED_PLAYERS} players.");
            TryDisconnectClient(clientId, "extra_player");
            return;
        }

        if (HasDuplicateWallet(clientId, walletPubkey))
        {
            _metadataMismatchDetected = true;
            Debug.LogError($"[MatchManager] Duplicate wallet detected ({ShortKey(walletPubkey)}). Rejecting client {clientId}.");
            TryDisconnectClient(clientId, "duplicate_wallet");
            return;
        }

        // Canonical game PDA must be shared by all players in this match.
        if (string.IsNullOrEmpty(_gamePda))
        {
            _gamePda = gamePda;
        }
        else if (!string.Equals(_gamePda, gamePda, StringComparison.Ordinal))
        {
            _metadataMismatchDetected = true;
            Debug.LogError(
                $"[MatchManager] Player {clientId} game PDA mismatch: expected {ShortKey(_gamePda)}, got {ShortKey(gamePda)}. " +
                "Rejecting player to avoid wrong settlement.");
            TryDisconnectClient(clientId, "game_pda_mismatch");
            return;
        }

        _players[clientId] = lobbyNet;
        Debug.Log($"[MatchManager] Player info stored: client={clientId}, wallet={ShortKey(walletPubkey)} game={ShortKey(gamePda)}. Players={_players.Count}");

        if (_players.Count >= REQUIRED_PLAYERS && CurrentState == MatchState.WaitingForPlayers)
        {
            CurrentState = MatchState.WaitingForReady;
            Debug.Log("[MatchManager] All players connected with metadata. Waiting for ready signals.");
        }
    }

    /// <summary>
    /// Called by PlayerLobbyNet.SubmitReadyServerRpc when a client marks ready.
    /// </summary>
    public void OnPlayerReady(ulong clientId)
    {
        if (!_players.TryGetValue(clientId, out var player) || player == null)
        {
            Debug.LogWarning($"[MatchManager] Ignoring ready from unknown client {clientId}.");
            return;
        }

        if (_metadataMismatchDetected)
        {
            Debug.LogWarning($"[MatchManager] Ignoring ready from {clientId} due to metadata mismatch flag.");
            return;
        }

        if (!HasValidPlayerInfo(player))
        {
            Debug.LogWarning($"[MatchManager] Ignoring ready from {clientId}: missing wallet/game metadata.");
            return;
        }

        _readyPlayers.Add(clientId);
        Debug.Log($"[MatchManager] Player {clientId} ready. Ready count: {_readyPlayers.Count}/{REQUIRED_PLAYERS}");

        TryStartMatch();
    }

    /// <summary>
    /// Called by ServerBootstrap when a client disconnects.
    /// </summary>
    public void OnPlayerDisconnected(ulong clientId)
    {
        _players.Remove(clientId);
        _readyPlayers.Remove(clientId);

        if (CurrentState == MatchState.InProgress && _players.Count < REQUIRED_PLAYERS)
        {
            if (_metadataMismatchDetected)
            {
                Debug.LogError("[MatchManager] Disconnect occurred with inconsistent player metadata. Forcing refund instead of winner settlement.");
                _ = FinalizeMatchAsync("broken", null, "disconnect_inconsistent_metadata", $"Client {clientId} disconnected");
                return;
            }

            // Remaining player wins by forfeit only if we still have one well-formed player.
            if (TryGetSingleRemainingPlayerWallet(out var winnerClientId, out var winnerPubkey))
            {
                Debug.Log($"[MatchManager] Player disconnected during match. Winner by forfeit: {winnerClientId} ({ShortKey(winnerPubkey)})");
                _ = FinalizeMatchAsync("winner", winnerPubkey, "disconnect_forfeit", $"Client {clientId} disconnected");
            }
            else
            {
                Debug.Log("[MatchManager] Could not determine a valid winner wallet after disconnect. Forcing refund.");
                _ = FinalizeMatchAsync("broken", null, "disconnect_unknown_winner", $"Client {clientId} disconnected");
            }
        }
        else if (CurrentState == MatchState.WaitingForPlayers || CurrentState == MatchState.WaitingForReady)
        {
            Debug.Log($"[MatchManager] Player {clientId} left before match started. Players remaining: {_players.Count}");
        }
    }

    /// <summary>
    /// Call this from game logic when the match ends with a winner.
    /// </summary>
    public void ReportMatchResult(string winnerWalletPubkey, string reasonCode, string reasonDetail = null)
    {
        if (CurrentState != MatchState.InProgress) return;

        winnerWalletPubkey = winnerWalletPubkey?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(winnerWalletPubkey) || !IsTrackedWallet(winnerWalletPubkey))
        {
            Debug.LogError("[MatchManager] ReportMatchResult received invalid/non-tracked winner wallet. Forcing refund.");
            _ = FinalizeMatchAsync("broken", null, "invalid_winner_wallet", reasonDetail);
            return;
        }

        Debug.Log($"[MatchManager] Match result: winner={ShortKey(winnerWalletPubkey)} reason={reasonCode}");
        _ = FinalizeMatchAsync("winner", winnerWalletPubkey, reasonCode, reasonDetail);
    }

    /// <summary>
    /// Call this from game logic when no winner should be paid (broken match / draw).
    /// </summary>
    public void ReportBrokenMatch(string reasonCode, string reasonDetail = null)
    {
        if (CurrentState != MatchState.InProgress) return;
        _ = FinalizeMatchAsync("broken", null, reasonCode, reasonDetail);
    }

    /// <summary>
    /// Returns wallet order by ascending client id.
    /// playerOneWallet corresponds to the first (lowest) client id.
    /// </summary>
    public bool TryGetOrderedPlayerWallets(out string playerOneWallet, out string playerTwoWallet)
    {
        playerOneWallet = null;
        playerTwoWallet = null;

        if (_players.Count < REQUIRED_PLAYERS) return false;

        var orderedClientIds = new List<ulong>(_players.Keys);
        orderedClientIds.Sort();
        if (orderedClientIds.Count < REQUIRED_PLAYERS) return false;

        if (!TryGetWalletForClient(orderedClientIds[0], out playerOneWallet)) return false;
        if (!TryGetWalletForClient(orderedClientIds[1], out playerTwoWallet)) return false;
        if (string.Equals(playerOneWallet, playerTwoWallet, StringComparison.Ordinal)) return false;

        return true;
    }

    void TryStartMatch()
    {
        if (CurrentState != MatchState.WaitingForPlayers && CurrentState != MatchState.WaitingForReady)
            return;

        if (_metadataMismatchDetected)
        {
            Debug.LogError("[MatchManager] Cannot start match: metadata mismatch detected.");
            return;
        }

        if (_players.Count < REQUIRED_PLAYERS || _readyPlayers.Count < REQUIRED_PLAYERS)
            return;

        // Verify all ready players have submitted info
        foreach (var readyId in _readyPlayers)
        {
            if (!_players.ContainsKey(readyId))
                return;
        }

        var wallets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in _players)
        {
            var player = kvp.Value;
            if (!HasValidPlayerInfo(player))
            {
                Debug.LogError($"[MatchManager] Cannot start: client {kvp.Key} has invalid player metadata.");
                return;
            }

            if (!string.Equals(player.GamePda.Trim(), _gamePda, StringComparison.Ordinal))
            {
                _metadataMismatchDetected = true;
                Debug.LogError($"[MatchManager] Cannot start: client {kvp.Key} has mismatched game PDA.");
                return;
            }

            if (!wallets.Add(player.WalletPubkey.Trim()))
            {
                _metadataMismatchDetected = true;
                Debug.LogError($"[MatchManager] Cannot start: duplicate wallet {ShortKey(player.WalletPubkey)}.");
                return;
            }
        }

        CurrentState = MatchState.InProgress;
        Debug.Log($"[MatchManager] Match starting! GamePDA: {ShortKey(_gamePda)}");
    }

    async System.Threading.Tasks.Task FinalizeMatchAsync(string outcome, string winnerPubkey, string reasonCode, string reasonDetail)
    {
        if (CurrentState == MatchState.Finished) return;
        CurrentState = MatchState.Finished;

        outcome = (outcome ?? "broken").Trim().ToLowerInvariant();
        reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "unspecified" : reasonCode.Trim();
        winnerPubkey = winnerPubkey?.Trim();

        if (string.IsNullOrEmpty(_gamePda))
        {
            Debug.LogError("[MatchManager] Cannot finalize - no game PDA.");
            ResetMatch();
            return;
        }

        if (outcome == "winner")
        {
            if (_metadataMismatchDetected || string.IsNullOrEmpty(winnerPubkey) || !IsTrackedWallet(winnerPubkey))
            {
                Debug.LogError("[MatchManager] Winner finalization requested with invalid winner/metadata. Falling back to broken/refund.");
                outcome = "broken";
                winnerPubkey = null;
                reasonCode = "invalid_winner_metadata";
            }
        }
        else
        {
            outcome = "broken";
            winnerPubkey = null;
        }

        string idempotencyKey = $"{_gamePda}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        Debug.Log($"[MatchManager] Finalizing match: outcome={outcome}, gamePda={ShortKey(_gamePda)}, winner={ShortKey(winnerPubkey)}");

        bool ok = await ServerFinalizeClient.Finalize(
            _gamePda, outcome, winnerPubkey, reasonCode, reasonDetail, idempotencyKey);

        if (ok)
        {
            Debug.Log("[MatchManager] Finalization request sent successfully.");
        }
        else
        {
            Debug.LogError("[MatchManager] Finalization request FAILED. Manual intervention may be needed.");
        }

        // Reset for next match
        ResetMatch();

        // Re-register as idle in server pool (dedicated-server builds only).
#if UNITY_SERVER
        if (ServerPoolHeartbeat.Instance != null)
            ServerPoolHeartbeat.Instance.MarkIdle();
#endif
    }

    bool HasValidPlayerInfo(PlayerLobbyNet player)
    {
        if (player == null) return false;
        return !string.IsNullOrWhiteSpace(player.WalletPubkey) && !string.IsNullOrWhiteSpace(player.GamePda);
    }

    bool TryGetSingleRemainingPlayerWallet(out ulong winnerClientId, out string winnerPubkey)
    {
        winnerClientId = 0;
        winnerPubkey = null;

        if (_players.Count != 1) return false;
        foreach (var kvp in _players)
        {
            if (!HasValidPlayerInfo(kvp.Value)) return false;
            if (!string.Equals(kvp.Value.GamePda.Trim(), _gamePda, StringComparison.Ordinal)) return false;
            winnerClientId = kvp.Key;
            winnerPubkey = kvp.Value.WalletPubkey.Trim();
            return !string.IsNullOrEmpty(winnerPubkey);
        }

        return false;
    }

    bool HasDuplicateWallet(ulong incomingClientId, string walletPubkey)
    {
        foreach (var kvp in _players)
        {
            if (kvp.Key == incomingClientId) continue;
            var existingWallet = kvp.Value?.WalletPubkey?.Trim() ?? string.Empty;
            if (string.Equals(existingWallet, walletPubkey, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    bool TryGetWalletForClient(ulong clientId, out string walletPubkey)
    {
        walletPubkey = null;
        if (!_players.TryGetValue(clientId, out var player) || !HasValidPlayerInfo(player)) return false;
        walletPubkey = player.WalletPubkey.Trim();
        return !string.IsNullOrEmpty(walletPubkey);
    }

    bool IsTrackedWallet(string walletPubkey)
    {
        if (string.IsNullOrWhiteSpace(walletPubkey)) return false;
        string target = walletPubkey.Trim();

        foreach (var player in _players.Values)
        {
            if (!HasValidPlayerInfo(player)) continue;
            if (string.Equals(player.WalletPubkey.Trim(), target, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    void TryDisconnectClient(ulong clientId, string reason)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId == NetworkManager.ServerClientId) return;

        Debug.LogWarning($"[MatchManager] Disconnecting client {clientId} ({reason}).");
        NetworkManager.Singleton.DisconnectClient(clientId);
    }

    static string ShortKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "(none)";
        if (key.Length <= 12) return key;
        return $"{key[..6]}…{key[^6..]}";
    }

    void ResetMatch()
    {
        _players.Clear();
        _readyPlayers.Clear();
        _gamePda = null;
        _metadataMismatchDetected = false;
        CurrentState = MatchState.WaitingForPlayers;

        // Disconnect all remaining clients so they return to lobby
        if (NetworkManager.Singleton != null)
        {
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (clientId != NetworkManager.ServerClientId)
                    NetworkManager.Singleton.DisconnectClient(clientId);
            }
        }

        Debug.Log("[MatchManager] Match reset. Ready for next match.");
    }
}
#endif
