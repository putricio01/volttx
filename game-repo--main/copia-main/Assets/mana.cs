#if !UNITY_SERVER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Solana.Unity.SDK;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core.Environments;

/// <summary>
/// Challenge board lobby controller.
///
/// Screens:
///   1. Wallet Connect  — "Connect Wallet" button
///   2. Challenge Board  — browse open challenges, create your own
///   3. Match Loading    — connecting to server, waiting for opponent
///
/// Flow (Creator):
///   ConnectWallet → CreateChallenge → on-chain create_game → register with backend
///   → server assigned immediately → connect → wait for opponent in-server → play
///
/// Flow (Acceptor):
///   ConnectWallet → browse challenges → Accept → on-chain join_game
///   → backend returns assigned server → connect → play
/// </summary>
public class mana : MonoBehaviour
{
    public enum LobbyState
    {
        Idle,                // Nothing connected
        WalletConnected,     // Phantom wallet connected — show challenge board
        CreatingChallenge,   // On-chain create_game tx in flight
        AcceptingChallenge,  // On-chain join_game tx in flight
        Connecting,          // Connecting to dedicated server
        GameReady            // Connected + ready for match
    }

    [Header("Screen 1: Wallet")]
    public Button connectWalletButton;
    public Text walletLabel;

    [Header("Screen 2: Challenge Board")]
    public GameObject challengeBoardPanel;
    public Button createChallengeButton;
    public InputField wagerInput;
    public Transform challengeListContainer;
    public GameObject challengeEntryPrefab;

    [Header("Screen 3: Match Loading")]
    public GameObject matchLoadingPanel;
    public Text statusText;

    [Header("Services")]
    public ttservise gameService;

    // ── internal state ──────────────────────────────────
    LobbyState _state = LobbyState.Idle;
    string _walletPubkey;
    string _gamePda;
    ulong  _matchId;
    BackendClient _backend;

    readonly List<GameObject> _challengeEntries = new List<GameObject>();
    float _refreshTimer;
    const float REFRESH_INTERVAL = 4f;

    // ── Public accessors for OnGUI debug buttons ─────
    public bool IsWalletConnected => _state >= LobbyState.WalletConnected;
    public string WalletPubkeyShort => TruncatePubkey(_walletPubkey);
    public string WalletPubkey => _walletPubkey;

    /// <summary>Called from NetworkButtons OnGUI to accept a challenge.</summary>
    public void AcceptChallengeFromDebug(BackendClient.ChallengeInfo challenge)
    {
        AcceptChallenge(challenge);
    }

    /// <summary>Called from NetworkButtons OnGUI to set wager before OnCreateChallenge.</summary>
    public void SetWagerFromDebug(string solAmount)
    {
        if (wagerInput != null)
            wagerInput.text = solAmount;
    }

    /// <summary>Called from NetworkButtons OnGUI to manually refresh challenge list.</summary>
    public void RefreshChallengesFromDebug()
    {
        _ = RefreshChallengeListAsync();
    }

    // ─────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────

    async void Start()
    {
        var config = GameConfig.Load();
        if (config != null)
            _backend = new BackendClient(config.backendUrl);
        else
            Debug.LogError("[Lobby] GameConfig not found — backend calls will fail.");

        // Wire button click handlers
        if (connectWalletButton != null)
            connectWalletButton.onClick.AddListener(OnConnectWallet);
        if (createChallengeButton != null)
            createChallengeButton.onClick.AddListener(OnCreateChallenge);

        RefreshUI();
        await InitUnityServicesAsync();
    }

    void Update()
    {
        // Auto-refresh challenge list while on the board
        if (_state == LobbyState.WalletConnected)
        {
            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = REFRESH_INTERVAL;
                _ = RefreshChallengeListAsync();
            }
        }
    }

    // ─────────────────────────────────────────────────────
    // Screen 1: Wallet Connect
    // ─────────────────────────────────────────────────────

    public async void OnConnectWallet()
    {
        SetStatus("Connecting wallet…");
        if (connectWalletButton != null)
            connectWalletButton.interactable = false;

        try
        {
            // LoginWalletAdapter only works on mobile/WebGL (Android, iOS, WebGL).
            // In Editor and standalone desktop builds, use InGameWallet for testing.
#if UNITY_EDITOR || (UNITY_STANDALONE && !UNITY_SERVER)
            // Always create a fresh random wallet in editor so each MPPM instance
            // gets its own unique keypair (they share PlayerPrefs).
            Debug.Log("[Lobby] Editor detected — creating fresh InGameWallet keypair for this instance.");
            var wallet = await Web3.Instance.CreateAccount(null, "devtest");
#elif UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS
            var wallet = await Web3.Instance.LoginWalletAdapter();
#else
            var wallet = await Web3.Instance.CreateAccount(null, "devtest");
#endif

            if (wallet == null || Web3.Account == null)
            {
                SetStatus("Wallet connection cancelled.");
                if (connectWalletButton != null)
                    connectWalletButton.interactable = true;
                return;
            }

            _walletPubkey = Web3.Account.PublicKey.Key;
            Debug.Log($"[Lobby] Wallet connected: {_walletPubkey}");

            if (walletLabel != null)
                walletLabel.text = TruncatePubkey(_walletPubkey);

            SetState(LobbyState.WalletConnected);
            SetStatus("");

            // Immediately fetch challenge list
            _ = RefreshChallengeListAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Wallet connect error: {e.Message}");
            SetStatus("Wallet error – try again.");
            if (connectWalletButton != null)
                connectWalletButton.interactable = true;
        }
    }

    // ─────────────────────────────────────────────────────
    // Screen 2: Challenge Board — Create
    // ─────────────────────────────────────────────────────

    public async void OnCreateChallenge()
    {
        if (_state != LobbyState.WalletConnected) return;

        string wagerText = wagerInput != null ? wagerInput.text.Trim() : "";
        if (!double.TryParse(wagerText, out double wagerSol) || wagerSol <= 0)
        {
            SetStatus("Enter a valid wager amount (SOL).");
            return;
        }

        ulong wagerLamports = (ulong)(wagerSol * 1_000_000_000);
        _matchId = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        SetState(LobbyState.CreatingChallenge);
        SetStatus("Sending create_game on-chain…");

        bool txOk = await gameService.CreateGameTransaction(wagerLamports, _matchId);
        if (!txOk)
        {
            SetStatus("create_game failed – try again.");
            SetState(LobbyState.WalletConnected);
            return;
        }

        _gamePda = gameService.LastGameAddress;
        SetStatus("Registering challenge…");

        var assignment = await _backend.RegisterChallenge(_gamePda, _walletPubkey, wagerLamports, _matchId);
        if (assignment == null)
        {
            SetStatus("Failed to register challenge with backend. On-chain game was created.");
            SetState(LobbyState.WalletConnected);
            return;
        }

        // Server assigned on creation — connect immediately
        SetState(LobbyState.Connecting);
        SetStatus($"Connecting to server {assignment.server_ip}:{assignment.server_port}…");

        bool connected = await ConnectToServerAsync(assignment.server_ip, assignment.server_port);
        if (connected)
        {
            SetState(LobbyState.GameReady);
            SetStatus("Connected! Waiting for opponent to join…");
            SendPlayerInfoToServer();
        }
        else
        {
            SetStatus("Server connection failed.");
            SetState(LobbyState.WalletConnected);
        }
    }

    // ─────────────────────────────────────────────────────
    // Screen 2: Challenge Board — Accept
    // ─────────────────────────────────────────────────────

    async void AcceptChallenge(BackendClient.ChallengeInfo challenge)
    {
        if (_state != LobbyState.WalletConnected) return;

        _gamePda = challenge.game_pda;
        SetState(LobbyState.AcceptingChallenge);
        SetStatus("Sending join_game on-chain…");

        bool txOk = await gameService.JoinGameTransaction(_gamePda);
        if (!txOk)
        {
            SetStatus("join_game failed – try again.");
            SetState(LobbyState.WalletConnected);
            return;
        }

        SetStatus("Requesting server assignment…");

        var assignment = await _backend.AcceptChallenge(_gamePda, _walletPubkey);
        if (assignment == null)
        {
            SetStatus("Server assignment failed – try again.");
            SetState(LobbyState.WalletConnected);
            return;
        }

        SetState(LobbyState.Connecting);
        SetStatus($"Connecting to server {assignment.server_ip}:{assignment.server_port}…");

        bool connected = await ConnectToServerAsync(assignment.server_ip, assignment.server_port);
        if (connected)
        {
            SetState(LobbyState.GameReady);
            SetStatus("Connected! Match starting…");
            SendPlayerInfoToServer();
        }
        else
        {
            SetStatus("Server connection failed.");
            SetState(LobbyState.WalletConnected);
        }
    }

    // ─────────────────────────────────────────────────────
    // Challenge list refresh
    // ─────────────────────────────────────────────────────

    async Task RefreshChallengeListAsync()
    {
        if (_backend == null) return;

        var challenges = await _backend.GetOpenChallenges();

        // Clear old entries
        foreach (var go in _challengeEntries)
        {
            if (go != null) Destroy(go);
        }
        _challengeEntries.Clear();

        if (challengeListContainer == null || challengeEntryPrefab == null) return;

        foreach (var c in challenges)
        {
            var entry = Instantiate(challengeEntryPrefab, challengeListContainer);
            entry.SetActive(true);
            _challengeEntries.Add(entry);

            // Populate entry UI
            var texts = entry.GetComponentsInChildren<Text>();
            if (texts.Length >= 2)
            {
                texts[0].text = $"{TruncatePubkey(c.creator_pubkey)}";
                texts[1].text = $"{c.WagerSol:F4} SOL";
            }
            else if (texts.Length == 1)
            {
                texts[0].text = $"{TruncatePubkey(c.creator_pubkey)} — {c.WagerSol:F4} SOL";
            }

            // If this is our own challenge, don't show Accept button
            var btn = entry.GetComponentInChildren<Button>();
            if (btn != null)
            {
                if (c.creator_pubkey == _walletPubkey)
                {
                    btn.interactable = false;
                    var btnText = btn.GetComponentInChildren<Text>();
                    if (btnText != null) btnText.text = "Waiting…";
                }
                else
                {
                    var captured = c;
                    btn.onClick.AddListener(() => AcceptChallenge(captured));
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────
    // Server connection
    // ─────────────────────────────────────────────────────

    async Task<bool> ConnectToServerAsync(string ip, ushort port)
    {
        try
        {
            var nm = NetworkManager.Singleton;
            var transport = nm != null ? nm.GetComponent<UnityTransport>() : null;

            if (transport == null)
            {
                Debug.LogWarning("[Lobby] No NetworkManager/transport.");
                return false;
            }

            TransportConfig.ApplyTransportConfig(force: true);

            // Dedicated server direct-connect uses raw UDP for localhost, LAN, and VPS IPs.
            transport.UseWebSockets = false;

            Time.fixedDeltaTime = 1f / 60f;
            transport.SetConnectionData(ip, port);
            nm.StartClient();

            // Poll up to 5s for connection
            float waited = 0f;
            while (!nm.IsConnectedClient && waited < 5f)
            {
                await Task.Delay(100);
                waited += 0.1f;
            }

            return nm.IsConnectedClient;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] ConnectToServerAsync error: {e.Message}");
            return false;
        }
    }

    void SendPlayerInfoToServer()
    {
        // Find our local player's PlayerLobbyNet and submit info
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) return;

        var localPlayerObj = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
        if (localPlayerObj == null)
        {
            Debug.LogWarning("[Lobby] Local player object not yet spawned — will retry.");
            // Retry after a short delay (player spawn may take a frame)
            _ = RetrySubmitPlayerInfoAsync();
            return;
        }

        var lobbyNet = localPlayerObj.GetComponent<PlayerLobbyNet>();
        if (lobbyNet != null)
        {
            lobbyNet.SubmitPlayerInfoServerRpc(_walletPubkey, _gamePda);
            lobbyNet.SubmitReadyServerRpc();
        }
        else
        {
            Debug.LogWarning("[Lobby] PlayerLobbyNet not found on local player.");
        }
    }

    async Task RetrySubmitPlayerInfoAsync()
    {
        // Wait up to 3s for player object to spawn
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(100);

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) return;

            var obj = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
            if (obj == null) continue;

            var lobbyNet = obj.GetComponent<PlayerLobbyNet>();
            if (lobbyNet != null)
            {
                lobbyNet.SubmitPlayerInfoServerRpc(_walletPubkey, _gamePda);
                lobbyNet.SubmitReadyServerRpc();
                return;
            }
        }

        Debug.LogError("[Lobby] Timed out waiting for PlayerLobbyNet.");
    }

    // ─────────────────────────────────────────────────────
    // UI helpers
    // ─────────────────────────────────────────────────────

    void SetState(LobbyState newState)
    {
        _state = newState;
        RefreshUI();
    }

    void RefreshUI()
    {
        // In this project scene, wallet connect is handled by NetworkButtons (OnGUI),
        // so Canvas lobby panels should stay hidden to avoid gray/white overlays.
        if (connectWalletButton == null)
        {
            if (challengeBoardPanel != null)
                challengeBoardPanel.SetActive(false);
            if (matchLoadingPanel != null)
                matchLoadingPanel.SetActive(false);
            return;
        }

        bool walletConnected = _state >= LobbyState.WalletConnected;
        bool onBoard = _state == LobbyState.WalletConnected;
        bool inMatchFlow = _state >= LobbyState.CreatingChallenge;

        // Screen 1: Wallet — visible only before wallet connected
        if (connectWalletButton != null)
            connectWalletButton.gameObject.SetActive(!walletConnected);

        // Screen 2: Challenge Board — visible when wallet connected and not in match flow
        if (challengeBoardPanel != null)
            challengeBoardPanel.SetActive(onBoard);

        if (createChallengeButton != null)
            createChallengeButton.interactable = onBoard;

        if (wagerInput != null)
            wagerInput.interactable = onBoard;

        // Screen 3: Match Loading — visible during match flow
        if (matchLoadingPanel != null)
            matchLoadingPanel.SetActive(inMatchFlow);
    }

    void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;

        if (!string.IsNullOrEmpty(msg))
            Debug.Log($"[Lobby] {msg}");
    }

    static string TruncatePubkey(string pk)
    {
        if (string.IsNullOrEmpty(pk) || pk.Length <= 12) return pk ?? "";
        return $"{pk[..6]}…{pk[^6..]}";
    }

    async Task InitUnityServicesAsync()
    {
        try
        {
            var options = new Unity.Services.Core.InitializationOptions()
                .SetEnvironmentName("production");
            await Unity.Services.Core.UnityServices.InitializeAsync(options);

            if (!Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
                await Unity.Services.Authentication.AuthenticationService.Instance.SignInAnonymouslyAsync();

            Debug.Log("[Lobby] Unity Services initialized.");
        }
        catch
        {
            Debug.LogWarning("[Lobby] Unity Services init failed (ok in offline/editor mode).");
        }
    }
}
#endif
