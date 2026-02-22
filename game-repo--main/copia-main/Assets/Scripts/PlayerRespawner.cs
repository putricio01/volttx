using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// Server-authoritative player respawning.
/// Handles both initial spawning and respawning after goals.
/// Works with dedicated server (server is NOT a player).
/// </summary>
public class PlayerRespawner : NetworkBehaviour
{
    public List<GameObject> Respawns = new List<GameObject>();

    public Transform playerOneSpawnPoint;
    public Transform playerTwoSpawnPoint;
    public Ball ball;

    public void RespawnPlayersAfterGoal()
    {
        RespawnPlayersServerRpc();
    }

    public void Update()
    {
        // Debug key — only on clients, not on server
        if (IsServer && !IsHost) return;

        if (Input.GetKeyDown(KeyCode.K) && IsOwner)
            RespawnPlayersServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RespawnPlayersServerRpc()
    {
        // On a dedicated server, the server is NOT a player.
        // Iterate all connected clients and assign spawn points by index.
        int playerIndex = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            bool isPlayerOne = (playerIndex == 0);
            RespawnPlayer(client.Key, isPlayerOne);
            playerIndex++;

            if (playerIndex >= 2) break; // 1v1 game
        }
    }

    private void RespawnPlayer(ulong clientId, bool isPlayerOne)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var networkClient))
        {
            var player = networkClient.PlayerObject;
            if (player == null) return;

            Vector3 spawnPosition = isPlayerOne ? playerOneSpawnPoint.position : playerTwoSpawnPoint.position;
            Quaternion spawnRotation = isPlayerOne ? playerOneSpawnPoint.rotation : playerTwoSpawnPoint.rotation;

            // Server: teleport the player and reset physics
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = spawnPosition;
                rb.rotation = spawnRotation;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                player.transform.position = spawnPosition;
                player.transform.rotation = spawnRotation;
            }

            // Reset prediction buffers on the owning client
            var networkController = player.GetComponent<CarNetworkController>();
            if (networkController != null)
            {
                networkController.ResetBuffers();
            }

            // Inform clients of the new position
            MovePlayerClientRpc(spawnPosition, spawnRotation, clientId);
        }
    }

    [ClientRpc(RequireOwnership = false)]
    private void MovePlayerClientRpc(Vector3 position, Quaternion rotation, ulong clientId)
    {
        // Server doesn't need client-side snap logic
        if (IsServer && !IsHost) return;

        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
            // Find our player and snap to position
            if ((int)clientId < Respawns.Count && Respawns[(int)clientId] != null)
            {
                var player = Respawns[(int)clientId];
                player.transform.position = position;
                player.transform.rotation = rotation;

                var rb = player.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                // Reset prediction buffers
                var networkController = player.GetComponent<CarNetworkController>();
                if (networkController != null)
                {
                    networkController.ResetBuffers();
                }
            }
        }
    }

    private NetworkObject GetClientNetworkObject(ulong clientId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var networkClient))
        {
            return networkClient.PlayerObject;
        }
        return null;
    }
}
