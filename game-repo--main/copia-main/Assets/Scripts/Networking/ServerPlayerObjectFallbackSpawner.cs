using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Defensive fallback for dedicated/editor server sessions.
/// If a connected client never receives its PlayerObject, spawn the configured
/// player prefab manually so the client still gets a camera and controls.
/// </summary>
public class ServerPlayerObjectFallbackSpawner : MonoBehaviour
{
    static ServerPlayerObjectFallbackSpawner _instance;

    NetworkManager _registeredNetworkManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null || FindFirstObjectByType<ServerPlayerObjectFallbackSpawner>() != null)
            return;

        var go = new GameObject(nameof(ServerPlayerObjectFallbackSpawner));
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ServerPlayerObjectFallbackSpawner>();
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            UnregisterCallbacks();
            return;
        }

        if (_registeredNetworkManager == nm)
            return;

        UnregisterCallbacks();
        _registeredNetworkManager = nm;
        _registeredNetworkManager.OnClientConnectedCallback += OnClientConnected;
    }

    void OnDestroy()
    {
        UnregisterCallbacks();
    }

    void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer)
            return;

        // Dedicated/editor server should not try to create a player for the server itself.
        if (!nm.IsHost && clientId == NetworkManager.ServerClientId)
            return;

        StartCoroutine(EnsurePlayerObjectExists(clientId));
    }

    IEnumerator EnsurePlayerObjectExists(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            yield break;

        var cfg = GameConfig.Load();
        float timeout = cfg != null ? cfg.GetDirectConnectPlayerSpawnTimeoutSeconds() : 4f;
        float deadline = Time.unscaledTime + timeout;

        while (Time.unscaledTime < deadline)
        {
            if (nm == null || !nm.IsServer)
                yield break;

            if (!nm.ConnectedClients.TryGetValue(clientId, out var client))
                yield break;

            if (client.PlayerObject != null)
                yield break;

            yield return null;
        }

        if (nm == null || !nm.IsServer)
            yield break;

        if (!nm.ConnectedClients.TryGetValue(clientId, out var connectedClient))
            yield break;

        if (connectedClient.PlayerObject != null)
            yield break;

        var playerPrefab = nm.NetworkConfig.PlayerPrefab;
        if (playerPrefab == null)
        {
            Debug.LogError("[ServerPlayerObjectFallbackSpawner] NetworkConfig.PlayerPrefab is null. Cannot recover missing PlayerObject.");
            yield break;
        }

        var go = Instantiate(playerPrefab);
        go.transform.rotation = Quaternion.identity; // Force upright — prefab may have rotated spawn children
        var playerNetworkObject = go.GetComponent<NetworkObject>();
        if (playerNetworkObject == null)
        {
            Debug.LogError("[ServerPlayerObjectFallbackSpawner] Player prefab does not contain a NetworkObject.");
            yield break;
        }

        playerNetworkObject.SpawnAsPlayerObject(clientId, false);
        Debug.LogWarning($"[ServerPlayerObjectFallbackSpawner] Spawned fallback PlayerObject for client {clientId}.");
    }

    void UnregisterCallbacks()
    {
        if (_registeredNetworkManager != null)
        {
            _registeredNetworkManager.OnClientConnectedCallback -= OnClientConnected;
            _registeredNetworkManager = null;
        }
    }
}
