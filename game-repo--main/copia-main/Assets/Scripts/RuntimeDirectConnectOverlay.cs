using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class RuntimeDirectConnectOverlay : MonoBehaviour
{
    [SerializeField] bool enableInEditor;
    [SerializeField] Rect panelRect = new Rect(10f, 10f, 360f, 330f);

    string _serverIp;
    string _serverPort;
    bool _isConnecting;

    // Diagnostic state
    float _connectStartTime;
    bool _wasConnectedClient;
    string _disconnectReason;
    readonly List<string> _logLines = new List<string>();
    const int MaxLogLines = 8;
    float _smoothedFps;
    CarNetworkController _cachedLocalCar;
    BallNetworkController _cachedBall;
    MobileControlsBootstrap _cachedMobileBootstrap;

    void Awake()
    {
        ApplyConfigDefaults();
    }

    void ApplyConfigDefaults()
    {
        var cfg = GameConfig.Load();
        if (cfg == null)
        {
            _serverIp = "127.0.0.1";
            _serverPort = "7777";
            return;
        }

        _serverIp = cfg.GetDirectConnectIp();
        _serverPort = cfg.GetDirectConnectPort().ToString();
    }

    bool ShouldShow()
    {
#if UNITY_EDITOR
        return enableInEditor;
#else
        return GameConfig.Load()?.showRuntimeDirectConnectOverlay ?? true;
#endif
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        float fps = 1f / dt;
        _smoothedFps = _smoothedFps <= 0f ? fps : Mathf.Lerp(_smoothedFps, fps, 0.1f);

        // Detect transition to connected
        if (nm.IsConnectedClient && !_wasConnectedClient)
        {
            _wasConnectedClient = true;
            float elapsed = Time.unscaledTime - _connectStartTime;
            AddLog($"Connected OK ({elapsed:F1}s)");
        }

        // Detect disconnect
        if (_wasConnectedClient && !nm.IsClient)
        {
            _wasConnectedClient = false;
            AddLog("Disconnected");
        }

        if (nm.IsClient && !nm.IsServer)
        {
            var localPlayerObject = nm.SpawnManager?.GetLocalPlayerObject();
            if (localPlayerObject != null)
                _cachedLocalCar = localPlayerObject.GetComponentInChildren<CarNetworkController>();
        }

        if (_cachedBall == null)
            _cachedBall = FindObjectOfType<BallNetworkController>();

        if (_cachedMobileBootstrap == null)
            _cachedMobileBootstrap = FindObjectOfType<MobileControlsBootstrap>();
    }

    void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
        }
    }

    void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }
    }

    void OnClientConnected(ulong clientId)
    {
        AddLog($"Client {clientId} connected");
    }

    void OnClientDisconnect(ulong clientId)
    {
        _disconnectReason = NetworkManager.Singleton.DisconnectReason;
        string reason = string.IsNullOrEmpty(_disconnectReason) ? "unknown" : _disconnectReason;
        AddLog($"Client {clientId} dc: {reason}");
    }

    void OnTransportFailure()
    {
        AddLog("Transport FAILED");
    }

    void OnGUI()
    {
        if (!ShouldShow()) return;

        GUILayout.BeginArea(panelRect, GUI.skin.box);
        GUILayout.Label("Direct Connect");

        if (NetworkManager.Singleton == null)
        {
            GUILayout.Label("Waiting for NetworkManager...");
            GUILayout.EndArea();
            return;
        }

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            GUILayout.Label("Server IP");
            _serverIp = GUILayout.TextField(_serverIp ?? string.Empty);

            GUILayout.Label("Port");
            _serverPort = GUILayout.TextField(_serverPort ?? string.Empty);

            if (_isConnecting)
            {
                GUILayout.Label("Connecting...");
            }
            else if (GUILayout.Button("Connect"))
            {
                Connect();
            }
        }
        else
        {
            string role = NetworkManager.Singleton.IsServer ? "Server" : "Client";
            if (NetworkManager.Singleton.IsHost) role = "Host";
            GUILayout.Label($"Role: {role}");

            bool connected = NetworkManager.Singleton.IsConnectedClient;
            GUILayout.Label($"Connected: {connected}");

            if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                // Show time since connect attempt
                if (!connected && _connectStartTime > 0f)
                {
                    float waiting = Time.unscaledTime - _connectStartTime;
                    GUILayout.Label($"Handshake: {waiting:F0}s (timeout ~10s)");
                }

                var localPlayerObject = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
                if (localPlayerObject != null)
                {
                    GUILayout.Label($"Player: {localPlayerObject.name}");
                }
                else if (connected)
                {
                    float sinceConnect = Time.unscaledTime - _connectStartTime;
                    GUILayout.Label($"Player: waiting spawn ({sinceConnect:F0}s)");
                }
                else
                {
                    GUILayout.Label("Player: not connected yet");
                }

                // Show spawned object count
                int spawnedCount = NetworkManager.Singleton.SpawnManager?.SpawnedObjectsList?.Count ?? 0;
                GUILayout.Label($"Spawned objects: {spawnedCount}");
                DrawRuntimeMetrics();
            }

            if (GUILayout.Button("Disconnect"))
            {
                NetworkManager.Singleton.Shutdown();
                _wasConnectedClient = false;
                AddLog("Manual disconnect");
            }
        }

        // Always show log at bottom
        if (_logLines.Count > 0)
        {
            GUILayout.Space(5);
            GUILayout.Label("-- Log --");
            foreach (var line in _logLines)
                GUILayout.Label(line);
        }

        GUILayout.EndArea();
    }

    void DrawRuntimeMetrics()
    {
        GUILayout.Space(6f);
        GUILayout.Label("-- Runtime --");
        string scaleLabel = _cachedMobileBootstrap != null ? $" scale {_cachedMobileBootstrap.DebugRenderScale:0.00}" : "";
        GUILayout.Label($"FPS {_smoothedFps:0.0} ({1000f / Mathf.Max(_smoothedFps, 0.01f):0.0} ms){scaleLabel}");

        var cfg = NetworkManager.Singleton?.NetworkConfig;
        uint tickRate = cfg?.TickRate ?? 0u;
        if (tickRate > 0)
            GUILayout.Label($"Net {tickRate}Hz ({1f / tickRate:0.0000}s)  Fixed {Time.fixedDeltaTime:0.0000}s");
        else
            GUILayout.Label($"Fixed {Time.fixedDeltaTime:0.0000}s");

        if (_cachedLocalCar != null)
        {
            GUILayout.Label($"Car c {_cachedLocalCar.DebugCorrectionCount}  h {_cachedLocalCar.DebugHardSnapCount}  e {_cachedLocalCar.DebugLastPosError:0.000}m/{_cachedLocalCar.DebugLastRotError:0.0}deg");
            GUILayout.Label($"Car back {_cachedLocalCar.DebugAdaptiveBackTimeMs:0}ms  jit {_cachedLocalCar.DebugArrivalJitterMs:0.0}ms  smooth {_cachedLocalCar.DebugVisualSmoothingOffset:0.000}");
        }
        else
        {
            GUILayout.Label("Car metrics: waiting local car");
        }

        if (_cachedBall != null)
        {
            GUILayout.Label($"Ball n {_cachedBall.DebugSnapshotCount}  x {_cachedBall.DebugExtrapolationCount}  active {(_cachedBall.DebugIsExtrapolating ? "yes" : "no")}  back {_cachedBall.DebugAdaptiveBackTimeMs:0}ms  jit {_cachedBall.DebugArrivalJitterMs:0.0}ms");
        }
        else
        {
            GUILayout.Label("Ball metrics: waiting ball");
        }
    }

    void Connect()
    {
        if (_isConnecting) return;

        if (!ushort.TryParse(_serverPort, out var port))
        {
            AddLog("Invalid port");
            return;
        }

        if (string.IsNullOrWhiteSpace(_serverIp))
        {
            AddLog("Empty IP");
            return;
        }

        _isConnecting = true;
        _connectStartTime = Time.unscaledTime;
        _wasConnectedClient = false;
        _disconnectReason = null;

        try
        {
            TransportConfig.ApplyTransportConfig(force: true);
            AddLog($"Connecting {_serverIp}:{port}");

            if (Kart.Multiplayer.Instance != null)
            {
                Kart.Multiplayer.Instance.DirectConnect(_serverIp.Trim(), port);
            }
            else
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    AddLog("No UnityTransport!");
                    return;
                }

                transport.UseWebSockets = false;
                Time.fixedDeltaTime = 1f / 60f;
                transport.SetConnectionData(_serverIp.Trim(), port);
                NetworkManager.Singleton.StartClient();
            }

            Debug.Log($"[RuntimeDirectConnect] Connecting to {_serverIp}:{port}");
        }
        finally
        {
            _isConnecting = false;
        }
    }

    void AddLog(string msg)
    {
        string timestamp = Time.unscaledTime.ToString("F0");
        _logLines.Add($"[{timestamp}] {msg}");
        while (_logLines.Count > MaxLogLines)
            _logLines.RemoveAt(0);
        Debug.Log($"[RuntimeDirectConnect] {msg}");
    }
}
