using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Server-authoritative game timer. Only the server ticks the timer.
/// Sends time updates to clients via ClientRpc.
/// Starts when the second player joins (1v1 game).
/// </summary>
public class GameTimer : NetworkBehaviour
{
    public gol lol;
    public gol2 lol2;
    public TMP_Text timerText;
    private NetworkVariable<float> timeRemaining = new NetworkVariable<float>(180f);

    private bool timerIsRunning = false;
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

        // Start when the second player joins
        if (NetworkManager.Singleton.ConnectedClients.Count == 2)
        {
            timeRemaining.Value = 180f; // Reset to 3 minutes
            timerIsRunning = true;
            playerRespawner.RespawnPlayersAfterGoal();
            lol.ResetScoreServerRpc();
            lol2.ResetScoreServerRpc();
            Debug.Log("[Server] Both players connected. Match starting!");
        }
    }

    void Update()
    {
        // Only the server ticks the timer (guard against pre-spawn calls)
        if (!IsSpawned || !IsServer) return;

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
        Debug.Log("[Server] Match ended. Shutting down...");

        // Notify clients before shutdown
        GameOverClientRpc();

        if (ball != null)
        {
            Destroy(ball.gameObject);
        }

        // Delay shutdown slightly so the ClientRpc has time to arrive
        Invoke(nameof(ShutdownServer), 1f);
    }

    void ShutdownServer()
    {
        NetworkManager.Singleton.Shutdown();
    }
}
