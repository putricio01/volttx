using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using Utilities;

namespace Kart {
    [System.Serializable]
    public enum EncryptionType {
        DTLS,
        WSS
    }

    /// <summary>
    /// Manages authentication, lobby creation/joining, and server connection.
    /// For dedicated server: lobbies store server IP/port instead of relay join code.
    /// Clients always call StartClient() — never StartHost().
    /// </summary>
    public class Multiplayer : MonoBehaviour {
        [Header("Lobby Settings")]
        [SerializeField] string lobbyName = "Lobby";
        [SerializeField] int maxPlayers = 3;

        [Header("Server Connection")]
        [SerializeField] string defaultServerIp = "127.0.0.1";
        [SerializeField] ushort defaultServerPort = 7777;

        public static Multiplayer Instance { get; private set; }

        public string PlayerId { get; private set; }
        public string PlayerName { get; private set; }

        Lobby currentLobby;

        const float k_lobbyHeartbeatInterval = 20f;
        const float k_lobbyPollInterval = 65f;
        const string k_keyServerIp = "ServerIp";
        const string k_keyServerPort = "ServerPort";

        CountdownTimer heartbeatTimer = new CountdownTimer(k_lobbyHeartbeatInterval);
        CountdownTimer pollForUpdatesTimer = new CountdownTimer(k_lobbyPollInterval);

        async Task Start() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(this);

            // Only authenticate and set up lobby timers on clients, not on the server editor.
            // Check NetworkManager — if it's already running as server, skip client-only setup.
            bool isServerOnly = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost;
            if (!isServerOnly)
            {
                await Authenticate();

                heartbeatTimer.OnTimerStop += () => {
                    HandleHeartbeatAsync();
                    if (!heartbeatTimer.IsRunning) heartbeatTimer.Start();
                };

                pollForUpdatesTimer.OnTimerStop += () => {
                    HandlePollForUpdatesAsync();
                    if (!pollForUpdatesTimer.IsRunning) pollForUpdatesTimer.Start();
                };
            }
        }

        // ====================================================================
        // AUTHENTICATION
        // ====================================================================

        async Task Authenticate() {
            await Authenticate("Player" + Random.Range(0, 1000));
        }

        async Task Authenticate(string playerName) {
            if (UnityServices.State == ServicesInitializationState.Uninitialized ||
                UnityServices.State == ServicesInitializationState.Initializing) {
                InitializationOptions options = new InitializationOptions();
                options.SetProfile(playerName);
                await UnityServices.InitializeAsync(options);
            }

            AuthenticationService.Instance.SignedIn += () => {
                Debug.Log("Signed in as " + AuthenticationService.Instance.PlayerId);
            };

            if (!AuthenticationService.Instance.IsSignedIn) {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                PlayerId = AuthenticationService.Instance.PlayerId;
                PlayerName = playerName;
            }
        }

        // ====================================================================
        // SERVER ALLOCATION (hosting-agnostic)
        // ====================================================================

        /// <summary>
        /// Allocate a dedicated server. Returns (ip, port).
        /// Override this method for different hosting providers.
        /// For now, returns the default local server address.
        /// </summary>
        public virtual async Task<(string ip, ushort port)> AllocateServer() {
            // TODO: For Unity Multiplay, call the Multiplay SDK to allocate a server.
            // TODO: For self-hosted, call your REST API to spin up a server process.
            // For now, return default (local testing):
            await Task.Yield(); // Simulate async
            return (defaultServerIp, defaultServerPort);
        }

        /// <summary>
        /// Connect to a dedicated server at the given IP/port.
        /// </summary>
        public void ConnectToServer(string ip, ushort port) {
            // Ensure transport queue size is configured before connecting
            TransportConfig.ApplyTransportConfig(force: true);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            // For local/direct connections, use raw UDP (not WebSocket/Relay)
            if (ip == "127.0.0.1" || ip == "localhost") {
                transport.UseWebSockets = false;
            }
            // Keep client physics step consistent with server/editor test setup.
            Time.fixedDeltaTime = 1f / 60f;
            transport.SetConnectionData(ip, port);
            NetworkManager.Singleton.StartClient();
            Debug.Log($"Connecting to dedicated server at {ip}:{port}");
        }

        // ====================================================================
        // LOBBY (for friends)
        // ====================================================================

        /// <summary>
        /// Create a lobby for friends. Allocates a server, stores its IP/port in lobby data.
        /// Both players connect as clients.
        /// </summary>
        public async Task CreateLobby() {
            try {
                // 1. Allocate a dedicated server
                var (serverIp, serverPort) = await AllocateServer();

                // 2. Create lobby
                CreateLobbyOptions options = new CreateLobbyOptions {
                    IsPrivate = false
                };

                currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
                Debug.Log("Created lobby: " + currentLobby.Name + " with code " + currentLobby.LobbyCode);

                heartbeatTimer.Start();
                pollForUpdatesTimer.Start();

                // 3. Store server IP/port in lobby data (replaces old relay join code)
                await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions {
                    Data = new Dictionary<string, DataObject> {
                        { k_keyServerIp, new DataObject(DataObject.VisibilityOptions.Member, serverIp) },
                        { k_keyServerPort, new DataObject(DataObject.VisibilityOptions.Member, serverPort.ToString()) }
                    }
                });

                // 4. Connect to the dedicated server as a client (NOT StartHost)
                ConnectToServer(serverIp, serverPort);

            } catch (LobbyServiceException e) {
                Debug.LogError("Failed to create lobby: " + e.Message);
            }
        }

        /// <summary>
        /// Quick join an existing lobby. Reads server IP/port from lobby data and connects.
        /// </summary>
        public async Task QuickJoinLobby() {
            try {
                currentLobby = await LobbyService.Instance.QuickJoinLobbyAsync();
                pollForUpdatesTimer.Start();

                if (currentLobby != null &&
                    currentLobby.Data.TryGetValue(k_keyServerIp, out var ipData) &&
                    currentLobby.Data.TryGetValue(k_keyServerPort, out var portData)) {

                    string serverIp = ipData.Value;
                    ushort serverPort = ushort.Parse(portData.Value);
                    ConnectToServer(serverIp, serverPort);
                } else {
                    Debug.LogError("Failed to retrieve server connection info from lobby.");
                }

            } catch (LobbyServiceException e) {
                Debug.LogError("Failed to quick join lobby: " + e.Message);
            }
        }

        /// <summary>
        /// Direct connect to a server (for testing without lobbies).
        /// </summary>
        public void DirectConnect(string ip = null, ushort port = 0) {
            ConnectToServer(ip ?? defaultServerIp, port == 0 ? defaultServerPort : port);
        }

        // ====================================================================
        // LOBBY MAINTENANCE
        // ====================================================================

        async Task HandleHeartbeatAsync() {
            if (currentLobby == null) return;

            try {
                await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
            } catch (LobbyServiceException e) {
                Debug.LogError("Failed to heartbeat lobby: " + e.Message);
            }
        }

        async Task HandlePollForUpdatesAsync() {
            if (currentLobby == null) return;

            try {
                Lobby lobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
            } catch (LobbyServiceException e) {
                Debug.LogError("Failed to poll for updates on lobby: " + e.Message);
            }
        }
    }
}
