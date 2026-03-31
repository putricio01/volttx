using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// Server-authoritative player respawning.
/// Handles both initial spawning and respawning after goals.
/// Works with dedicated server (server is NOT a player).
/// </summary>
public class PlayerRespawner : NetworkBehaviour
{
    const int RequiredPlayers = 2;
    const float InitialRespawnRetryInterval = 0.1f;
    const int InitialRespawnMaxRetries = 40;

    public List<GameObject> Respawns = new List<GameObject>();

    public Transform playerOneSpawnPoint;
    public Transform playerTwoSpawnPoint;
    [Header("Forced Spawn Position")]
    public float spawnHeightY = 0.8f;
    public float playerOneSpawnZ = -9.5f;
    public float playerTwoSpawnZ = 9.5f;
    public Ball ball;

    Coroutine _initialRespawnRoutine;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        QueueInitialRespawn();
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

        if (_initialRespawnRoutine != null)
        {
            StopCoroutine(_initialRespawnRoutine);
            _initialRespawnRoutine = null;
        }
    }

    public void RespawnPlayersAfterGoal()
    {
        if (IsServer)
        {
            RespawnPlayersServerInternal();
            return;
        }

        RespawnPlayersServerRpc();
    }

    public void Update()
    {
        // Debug key — only on clients, not on server
        if (IsServer && !IsHost) return;

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.K) && IsOwner)
            RespawnPlayersServerRpc();
#endif
    }

    [ServerRpc(RequireOwnership = false)]
    private void RespawnPlayersServerRpc()
    {
        RespawnPlayersServerInternal();
    }

    private void RespawnPlayersServerInternal()
    {
        // Assign player slots deterministically by ascending clientId.
        // Dedicated server: exclude ServerClientId (it's not a player).
        // Host mode: include ServerClientId because host is a real player.
        var orderedClientIds = new List<ulong>();
        bool isDedicatedServer = NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsServer
            && !NetworkManager.Singleton.IsHost;

        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (isDedicatedServer && clientId == NetworkManager.ServerClientId) continue;
            orderedClientIds.Add(clientId);
        }
        orderedClientIds.Sort();

        int playerIndex = 0;
        foreach (var clientId in orderedClientIds)
        {
            bool isPlayerOne = playerIndex == 0;
            RespawnPlayer(clientId, isPlayerOne);
            playerIndex++;

            if (playerIndex >= 2) break; // 1v1 game
        }
    }

    private void OnClientConnected(ulong _)
    {
        QueueInitialRespawn();
    }

    private void QueueInitialRespawn()
    {
        if (!IsServer) return;

        if (_initialRespawnRoutine != null)
            StopCoroutine(_initialRespawnRoutine);

        _initialRespawnRoutine = StartCoroutine(TryRespawnWhenReady());
    }

    private IEnumerator TryRespawnWhenReady()
    {
        for (int i = 0; i < InitialRespawnMaxRetries; i++)
        {
            if (HasRequiredPlayerObjectsReady())
            {
                RespawnPlayersServerInternal();
                _initialRespawnRoutine = null;
                yield break;
            }

            yield return new WaitForSeconds(InitialRespawnRetryInterval);
        }

        Debug.LogWarning("[PlayerRespawner] Initial spawn reposition timed out waiting for player objects.");
        _initialRespawnRoutine = null;
    }

    private bool HasRequiredPlayerObjectsReady()
    {
        if (NetworkManager.Singleton == null) return false;

        var orderedClientIds = new List<ulong>();
        bool isDedicatedServer = NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost;

        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (isDedicatedServer && clientId == NetworkManager.ServerClientId) continue;
            orderedClientIds.Add(clientId);
        }

        orderedClientIds.Sort();
        if (orderedClientIds.Count < RequiredPlayers) return false;

        for (int i = 0; i < RequiredPlayers; i++)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(orderedClientIds[i], out var networkClient))
                return false;
            if (networkClient.PlayerObject == null)
                return false;
        }

        return true;
    }

    private void RespawnPlayer(ulong clientId, bool isPlayerOne)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var networkClient))
        {
            var player = networkClient.PlayerObject;
            if (player == null) return;

            Vector3 spawnPosition = isPlayerOne ? playerOneSpawnPoint.position : playerTwoSpawnPoint.position;
            spawnPosition.y = spawnHeightY;
            spawnPosition.z = isPlayerOne ? playerOneSpawnZ : playerTwoSpawnZ;
            // Force upright spawn — ignore spawn point rotation to avoid flipped cars
            Quaternion spawnRotation = Quaternion.identity;

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
            Debug.Log($"[PlayerRespawner] Teleported client {clientId} to {spawnPosition} (slot {(isPlayerOne ? 1 : 2)}).");
        }
    }

    [ClientRpc(RequireOwnership = false)]
    private void MovePlayerClientRpc(Vector3 position, Quaternion rotation, ulong clientId)
    {
        // Server doesn't need client-side snap logic
        if (IsServer && !IsHost) return;

        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
            GameObject player = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject()?.gameObject;
            if (player == null && (int)clientId < Respawns.Count)
                player = Respawns[(int)clientId];

            // Find our player and snap to position
            if (player != null)
            {
                player.transform.position = position;
                player.transform.rotation = rotation;

                var rb = player.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = position;
                    rb.rotation = rotation;  // Sync physics body with visual — prevents upside-down spawn
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
