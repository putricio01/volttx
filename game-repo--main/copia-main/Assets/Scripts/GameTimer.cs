using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Server-authoritative game timer. Only the server ticks the timer.
/// Sends time updates to clients via ClientRpc.
/// Dedicated server starts only after MatchManager validates both players as ready.
/// Host/editor fallback starts when the second player joins (1v1 game).
/// </summary>
public class GameTimer : NetworkBehaviour
{
    public gol lol;
    public gol2 lol2;
    public TMP_Text timerText;
    private NetworkVariable<float> timeRemaining = new NetworkVariable<float>(180f);

    private bool timerIsRunning = false;
    private bool roundStarted = false;
    public Ball ball;
    public PlayerRespawner playerRespawner;

    private void Start()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        if (playerRespawner != null)
            playerRespawner.RespawnPlayersAfterGoal();

#if UNITY_SERVER || UNITY_EDITOR
        // Server-authoritative path (dedicated or editor-server):
        // wait until MatchManager validates metadata and both players are ready.
        if (NetworkManager.Singleton.ConnectedClients.Count == 2)
            Debug.Log("[Server] Two clients connected. Waiting for MatchManager InProgress before starting timer.");
#else
        // Host/editor fallback: start when second player connects.
        if (NetworkManager.Singleton.ConnectedClients.Count == 2)
            StartRound();
#endif
    }

    void Update()
    {
        // Only the server ticks the timer (guard against pre-spawn calls)
        if (!IsSpawned || !IsServer) return;

        TryStartRoundIfReady();

        if (timerIsRunning && timeRemaining.Value > 0)
        {
            timeRemaining.Value -= Time.deltaTime;
            UpdateTimerDisplay();
        }
        else if (timerIsRunning)
        {
            timerIsRunning = false;
            EndGame();
        }
    }

    void TryStartRoundIfReady()
    {
        if (timerIsRunning || roundStarted) return;

#if UNITY_SERVER || UNITY_EDITOR
        if (MatchManager.Instance == null) return;
        if (MatchManager.Instance.CurrentState != MatchManager.MatchState.InProgress) return;
#else
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.ConnectedClients.Count < 2) return;
#endif

        StartRound();
    }

    void StartRound()
    {
        if (roundStarted) return;

        roundStarted = true;
        timeRemaining.Value = 180f; // Reset to 3 minutes
        timerIsRunning = true;
        if (playerRespawner != null) playerRespawner.RespawnPlayersAfterGoal();
        if (lol != null) lol.ResetScoreServerRpc();
        if (lol2 != null) lol2.ResetScoreServerRpc();
        Debug.Log("[Server] Match round started.");
    }

    void UpdateTimerDisplay()
    {
        int minutes = Mathf.FloorToInt(timeRemaining.Value / 60);
        int seconds = Mathf.FloorToInt(timeRemaining.Value % 60);
        TimeUpdateClientRpc(minutes, seconds);
    }

    [ClientRpc]
    private void TimeUpdateClientRpc(int min, int sec)
    {
        // Server doesn't have UI — only update on clients
        if (IsServer && !IsHost) return;

        if (timerText != null)
            timerText.text = $"{min:00}:{sec:00}";
    }

    /// <summary>
    /// Notify all clients the game is over before shutting down.
    /// </summary>
    [ClientRpc]
    private void GameOverClientRpc()
    {
        // Server doesn't need game over UI
        if (IsServer && !IsHost) return;

        Debug.Log("[Client] Game over! Returning to menu...");
        // TODO: Load menu scene or show game over UI
    }

    void EndGame()
    {
        Debug.Log("[Server] Match ended. Finalizing result...");

#if UNITY_SERVER || UNITY_EDITOR
        FinalizeMatchFromScore();
#endif

        // Notify clients before shutdown
        GameOverClientRpc();

        if (ball != null)
        {
            Destroy(ball.gameObject);
        }

        // Delay shutdown so finalization request and GameOver RPC can be sent.
        Invoke(nameof(ShutdownServer), 2f);
    }

    void ShutdownServer()
    {
        NetworkManager.Singleton.Shutdown();
    }

#if UNITY_SERVER || UNITY_EDITOR
    void FinalizeMatchFromScore()
    {
        if (MatchManager.Instance == null)
        {
            Debug.LogError("[GameTimer] MatchManager missing at match end. Cannot finalize deterministically.");
            return;
        }

        int playerOneScore = lol != null ? lol.CurrentScore : 0;
        int playerTwoScore = lol2 != null ? lol2.CurrentScore : 0;

        if (!MatchManager.Instance.TryGetOrderedPlayerWallets(out var playerOneWallet, out var playerTwoWallet))
        {
            MatchManager.Instance.ReportBrokenMatch(
                "timer_end_missing_wallets",
                $"score={playerOneScore}-{playerTwoScore}");
            return;
        }

        if (playerOneScore > playerTwoScore)
        {
            MatchManager.Instance.ReportMatchResult(
                playerOneWallet,
                "timer_end_points",
                $"score={playerOneScore}-{playerTwoScore}");
        }
        else if (playerTwoScore > playerOneScore)
        {
            MatchManager.Instance.ReportMatchResult(
                playerTwoWallet,
                "timer_end_points",
                $"score={playerOneScore}-{playerTwoScore}");
        }
        else
        {
            MatchManager.Instance.ReportBrokenMatch(
                "timer_end_draw",
                $"score={playerOneScore}-{playerTwoScore}");
        }
    }
#endif
}
