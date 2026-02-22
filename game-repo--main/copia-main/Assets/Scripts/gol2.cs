using UnityEngine;
using Unity.Netcode;
using TMPro;

/// <summary>
/// Goal trigger for Player Two's goal. Server-authoritative scoring.
/// </summary>
public class gol2 : NetworkBehaviour
{
    private NetworkVariable<int> score = new NetworkVariable<int>();
    public Ball ball;
    public PlayerRespawner playerRespawner;
    public TextMeshProUGUI scoreText;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            score.Value = 0;
            UpdateScoreClientRpc(score.Value);
        }
    }

    /// <summary>
    /// Only the server detects goals (server runs authoritative ball physics).
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Ball"))
        {
            score.Value += 1;
            ball.Respawn();
            UpdateScoreClientRpc(score.Value);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void IncrementScoreServerRpc()
    {
        if (ball.Respawn())
        {
            score.Value += 1;
        }
        UpdateScoreClientRpc(score.Value);
    }

    [ClientRpc]
    private void UpdateScoreClientRpc(int newScore)
    {
        // Update UI only on clients (server has no UI in headless/editor-server mode)
        if (!IsServer || IsHost)
        {
            if (scoreText != null)
                scoreText.text = "player2:-" + newScore.ToString();
        }

        // Respawn is server-only — avoid double-call from clients
        if (IsServer)
            playerRespawner.RespawnPlayersAfterGoal();
    }

    [ServerRpc(RequireOwnership = false)]
    public void ResetScoreServerRpc()
    {
        score.Value = 0;
        UpdateScoreClientRpc(score.Value);
    }
}
