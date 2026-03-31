using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NetworkBehaviour on the player prefab (CubeController).
/// Clients submit their wallet pubkey and game PDA to the server after connecting.
/// Server stores this info and forwards to MatchManager.
/// </summary>
public class PlayerLobbyNet : NetworkBehaviour
{
    public string WalletPubkey { get; private set; }
    public string GamePda { get; private set; }
    public bool IsReady { get; private set; }

    /// <summary>
    /// Client calls this after connecting to submit their wallet pubkey and game PDA.
    /// </summary>
    [ServerRpc]
    public void SubmitPlayerInfoServerRpc(string walletPubkey, string gamePda, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[PlayerLobbyNet] Rejecting spoofed info submit from {rpcParams.Receive.SenderClientId} for owner {OwnerClientId}.");
            return;
        }

        walletPubkey = walletPubkey?.Trim() ?? string.Empty;
        gamePda = gamePda?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(walletPubkey) || string.IsNullOrEmpty(gamePda))
        {
            Debug.LogWarning($"[PlayerLobbyNet] Ignoring invalid player info for {OwnerClientId}: wallet/gamePda empty.");
            return;
        }

        // Lock player metadata for this spawned player object.
        if (!string.IsNullOrEmpty(WalletPubkey) || !string.IsNullOrEmpty(GamePda))
        {
            bool sameWallet = string.Equals(WalletPubkey, walletPubkey, System.StringComparison.Ordinal);
            bool sameGame = string.Equals(GamePda, gamePda, System.StringComparison.Ordinal);
            if (!sameWallet || !sameGame)
            {
                Debug.LogError(
                    $"[PlayerLobbyNet] Player {OwnerClientId} attempted to mutate lobby info. " +
                    $"existing=({ShortKey(WalletPubkey)}, {ShortKey(GamePda)}) new=({ShortKey(walletPubkey)}, {ShortKey(gamePda)}). Ignoring.");
            }
            return;
        }

        WalletPubkey = walletPubkey;
        GamePda = gamePda;

        Debug.Log($"[PlayerLobbyNet] Player {OwnerClientId} submitted info: wallet={ShortKey(walletPubkey)} gamePda={ShortKey(gamePda)}");

#if UNITY_SERVER || UNITY_EDITOR
        if (MatchManager.Instance != null)
            MatchManager.Instance.OnPlayerInfoReceived(OwnerClientId, this);
#endif
    }

    /// <summary>
    /// Client calls this when ready to start the match.
    /// </summary>
    [ServerRpc]
    public void SubmitReadyServerRpc()
    {
        IsReady = true;

        Debug.Log($"[PlayerLobbyNet] Player {OwnerClientId} marked ready.");

#if UNITY_SERVER || UNITY_EDITOR
        if (MatchManager.Instance != null)
            MatchManager.Instance.OnPlayerReady(OwnerClientId);
#endif
    }

    static string ShortKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "(empty)";
        if (key.Length <= 12) return key;
        return $"{key[..6]}…{key[^6..]}";
    }
}
