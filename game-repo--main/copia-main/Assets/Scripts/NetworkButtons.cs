using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Debug buttons for network testing.
/// Server build: shows "Start Server" button.
/// Client build: shows connect buttons + editor-only "Start Server (Editor)" for local testing.
/// </summary>
public class NetworkButtons : MonoBehaviour
{
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 400));

        if (NetworkManager.Singleton == null)
        {
            GUILayout.Label("Waiting for NetworkManager...");
            GUILayout.EndArea();
            return;
        }

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
#if UNITY_SERVER
            if (GUILayout.Button("Start Server"))
            {
                TransportConfig.ApplyTransportConfig();
                NetworkManager.Singleton.StartServer();
            }
#else

#if UNITY_EDITOR
            // ── EDITOR-ONLY: Start a local server inside the editor ──
            GUILayout.Label("── Editor Testing ──");

            if (GUILayout.Button("Start Server (Editor)"))
            {
                // Ensure transport is configured before starting
                TransportConfig.ApplyTransportConfig(force: true);

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                // Force direct UDP connection for local testing (scene may have WebSocket/Relay set)
                transport.UseWebSockets = false;
                transport.SetConnectionData("127.0.0.1", 7777);
                Time.fixedDeltaTime = 1f / 60f;
                NetworkManager.Singleton.StartServer();

                // Disable cameras, audio, and UI — server is headless, just like a real dedicated server
                DisableClientOnlyObjects();

                Debug.Log("[Editor] Started local server on 127.0.0.1:7777 (headless)");
            }

            if (GUILayout.Button("Start Host (Editor - Solo Test)"))
            {
                // Ensure transport is configured before starting
                TransportConfig.ApplyTransportConfig(force: true);

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.UseWebSockets = false;
                transport.SetConnectionData("127.0.0.1", 7777);
                Time.fixedDeltaTime = 1f / 60f;
                NetworkManager.Singleton.StartHost();
                Debug.Log("[Editor] Started as Host on 127.0.0.1:7777 (server + client in one)");
            }

            GUILayout.Space(10);
            GUILayout.Label("── Client Connection ──");
#endif

            if (GUILayout.Button("Connect to Server (127.0.0.1:7777)"))
            {
                // Ensure transport is configured before connecting
                TransportConfig.ApplyTransportConfig(force: true);

                // Force direct UDP for local testing (scene may have WebSocket/Relay)
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.UseWebSockets = false;
                Time.fixedDeltaTime = 1f / 60f;

                if (Kart.Multiplayer.Instance != null)
                    Kart.Multiplayer.Instance.DirectConnect();
                else
                {
                    transport.SetConnectionData("127.0.0.1", 7777);
                    NetworkManager.Singleton.StartClient();
                }
            }

           
#endif
        }
        else
        {
            // Show status when connected
            string role = NetworkManager.Singleton.IsServer ? "Server" : "Client";
            if (NetworkManager.Singleton.IsHost) role = "Host";
            GUILayout.Label($"Running as: {role}");
            GUILayout.Label($"Connected clients: {NetworkManager.Singleton.ConnectedClients.Count}");

            if (GUILayout.Button("Disconnect"))
            {
                NetworkManager.Singleton.Shutdown();
                Debug.Log("Disconnected from network.");
            }
        }

        GUILayout.EndArea();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Disable all cameras, audio, and UI on the server editor — just like a real dedicated server.
    /// Prevents keyboard/mouse input from being captured by the game view.
    /// </summary>
    static void DisableClientOnlyObjects()
    {
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            cam.gameObject.SetActive(false);

        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            listener.enabled = false;

        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            canvas.enabled = false;
    }
#endif
}
