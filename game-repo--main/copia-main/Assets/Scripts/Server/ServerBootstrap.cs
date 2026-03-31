using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Dedicated server entry point. Only compiled into server builds.
/// Disables all client-only objects, configures transport, and starts the server.
/// </summary>
public class ServerBootstrap : MonoBehaviour
{
#if UNITY_SERVER
    [SerializeField] ushort port = 7777;

    async void Start()
    {
        Application.targetFrameRate = 60;
        Time.fixedDeltaTime = 1f / 60f;

        Debug.Log("[Server] Dedicated server starting...");

        // Disable all rendering and audio (server is headless)
        DisableClientOnlyObjects();

        // Create server-only singletons at runtime
        // (these scripts are #if UNITY_SERVER so they can't be placed in editor)
        var mmGo = new GameObject("MatchManager");
        mmGo.AddComponent<MatchManager>();
        DontDestroyOnLoad(mmGo);

        var hbGo = new GameObject("ServerPoolHeartbeat");
        hbGo.AddComponent<ServerPoolHeartbeat>();
        DontDestroyOnLoad(hbGo);

        // Check for port override from environment (cloud hosting)
        string portEnv = System.Environment.GetEnvironmentVariable("PORT");
        if (!string.IsNullOrEmpty(portEnv) && ushort.TryParse(portEnv, out ushort envPort))
        {
            port = envPort;
            Debug.Log($"[Server] Using port from environment: {port}");
        }

        // Configure transport to listen for direct connections
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.UseWebSockets = false;
        transport.SetConnectionData("0.0.0.0", port);

        // Register callbacks
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Start as dedicated server (not host — server is NOT a player)
        NetworkManager.Singleton.StartServer();

        Debug.Log($"[Server] Dedicated server started on port {port}. Waiting for players...");
    }

    void OnClientConnected(ulong clientId)
    {
        int playerCount = NetworkManager.Singleton.ConnectedClients.Count;
        Debug.Log($"[Server] Client {clientId} connected. Total players: {playerCount}");

        // Mark server as busy once first player connects
        if (ServerPoolHeartbeat.Instance != null && playerCount == 1)
            ServerPoolHeartbeat.Instance.MarkBusy();
    }

    void OnClientDisconnected(ulong clientId)
    {
        int playerCount = NetworkManager.Singleton.ConnectedClients.Count;
        Debug.Log($"[Server] Client {clientId} disconnected. Remaining players: {playerCount}");

        // Delegate disconnect handling to MatchManager
        if (MatchManager.Instance != null)
            MatchManager.Instance.OnPlayerDisconnected(clientId);
    }

    void DisableClientOnlyObjects()
    {
        // Disable all cameras
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            cam.gameObject.SetActive(false);

        // Disable all audio listeners
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            listener.enabled = false;

        // Disable all canvas renderers (UI)
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            canvas.enabled = false;

        Debug.Log("[Server] Disabled cameras, audio, and UI for headless operation.");
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
#endif
}
