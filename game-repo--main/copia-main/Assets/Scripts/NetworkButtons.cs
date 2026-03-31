using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Debug buttons for network testing — EDITOR ONLY.
/// Includes wallet connect + challenge flow for testing in MPPM.
/// Production builds use the challenge board Canvas UI (mana.cs).
/// Server builds use ServerBootstrap for automatic startup.
/// </summary>
public class NetworkButtons : MonoBehaviour
{
#if UNITY_EDITOR
    private mana _manaRef;
    private string _wagerAmount = "0.01";
    private List<BackendClient.ChallengeInfo> _cachedChallenges = new();
    private float _challengeRefreshTimer;
    private const float CHALLENGE_REFRESH_INTERVAL = 3f;
    private bool _isConnecting;

    void Start()
    {
        _manaRef = FindFirstObjectByType<mana>();
    }

    void Update()
    {
        // Auto-refresh challenge list when wallet connected and not yet in a game
        if (_manaRef != null && _manaRef.IsWalletConnected
            && NetworkManager.Singleton != null
            && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            _challengeRefreshTimer -= Time.deltaTime;
            if (_challengeRefreshTimer <= 0f)
            {
                _challengeRefreshTimer = CHALLENGE_REFRESH_INTERVAL;
                _ = RefreshChallengesAsync();
            }
        }
    }

    async Task RefreshChallengesAsync()
    {
        try
        {
            var challenges = LocalChallengeStore.GetOpenChallenges();
            if (challenges != null)
                _cachedChallenges = challenges;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkButtons] Refresh failed: {e.Message}");
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 750));

        if (NetworkManager.Singleton == null)
        {
            GUILayout.Label("Waiting for NetworkManager...");
            GUILayout.EndArea();
            return;
        }

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            // ── Server controls ──
            GUILayout.Label("── Server ──");

            if (GUILayout.Button("Start Server (Editor)"))
            {
                TransportConfig.ApplyTransportConfig(force: true);
                EnsureEditorServerSystems();

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.UseWebSockets = false;
                transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
                Time.fixedDeltaTime = 1f / 60f;
                NetworkManager.Singleton.StartServer();

                DisableClientOnlyObjects();

                var cfg = GameConfig.Load();
                var lanIp = cfg != null ? cfg.externalDirectConnectIp : "set GameConfig externalDirectConnectIp";
                Debug.Log($"[Editor] Started local server on 127.0.0.1:7777 (listen 0.0.0.0, LAN target {lanIp}:7777)");
            }

            GUILayout.Space(10);

            // ── Wallet + Challenge flow ──
            GUILayout.Label("── Wallet & Challenge ──");

            if (_manaRef == null)
                _manaRef = FindFirstObjectByType<mana>();

            if (_manaRef != null)
            {
                string walletStatus = GetWalletStatus();
                GUILayout.Label($"Wallet: {walletStatus}");

                if (!_manaRef.IsWalletConnected)
                {
                    if (GUILayout.Button("Connect Wallet"))
                    {
                        _manaRef.OnConnectWallet();
                    }
                }
                else
                {
                    // Wager input
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Wager (SOL):", GUILayout.Width(80));
                    _wagerAmount = GUILayout.TextField(_wagerAmount, GUILayout.Width(80));
                    GUILayout.EndHorizontal();

                    // ── Full on-chain challenge (needs funded wallet) ──
                    if (GUILayout.Button("Create Challenge (On-Chain)"))
                    {
                        _manaRef.SetWagerFromDebug(_wagerAmount);
                        _manaRef.OnCreateChallenge();
                    }

                    GUILayout.Space(5);

                    if (GUILayout.Button("Refresh Challenges"))
                    {
                        _ = RefreshChallengesAsync();
                    }

                    // ── Open challenges list ──
                    GUILayout.Space(5);
                    GUILayout.Label($"── Open Challenges (store:{LocalChallengeStore.TotalCount} cached:{_cachedChallenges.Count}) ──");

                    if (_cachedChallenges.Count == 0)
                    {
                        GUILayout.Label("(no open challenges)");
                    }
                    else
                    {
                        foreach (var c in _cachedChallenges)
                        {
                            string pubShort = c.creator_pubkey.Length > 12
                                ? $"{c.creator_pubkey[..6]}…{c.creator_pubkey[^6..]}"
                                : c.creator_pubkey;
                            string label = $"{pubShort} — {c.WagerSol:F4} SOL";

                            bool isOwn = _manaRef.WalletPubkey != null
                                         && c.creator_pubkey == _manaRef.WalletPubkey;

                            if (isOwn)
                            {
                                GUILayout.Label($"  [yours] {label}");
                            }
                            else
                            {
                                if (!_isConnecting && GUILayout.Button($"Accept: {label}"))
                                {
                                    AcceptTestChallenge(c);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                GUILayout.Label("(mana component not found)");
            }

            GUILayout.Space(10);

            // ── Direct connection ──
            GUILayout.Label("── Direct Connect ──");

            if (GUILayout.Button("Connect to Server (127.0.0.1:7777)"))
            {
                ConnectToEditorServer();
            }
        }
        else
        {
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

    // ── Challenge helpers ──────────────────

    void CreateTestChallenge()
    {
        string walletPub = _manaRef.WalletPubkey;
        if (string.IsNullOrEmpty(walletPub))
        {
            Debug.LogError("[NetworkButtons] Wallet not connected — can't create challenge.");
            return;
        }

        if (!double.TryParse(_wagerAmount, out double wagerSol) || wagerSol <= 0)
        {
            Debug.LogError("[NetworkButtons] Invalid wager amount.");
            return;
        }

        ulong wagerLamports = (ulong)(wagerSol * 1_000_000_000);
        ulong matchId = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Generate a unique fake PDA for editor testing
        string fakePda = $"test_{walletPub[..8]}_{matchId}";

        var assignment = LocalChallengeStore.RegisterChallenge(fakePda, walletPub, wagerLamports, matchId);
        if (assignment != null)
        {
            Debug.Log($"[NetworkButtons] Test challenge created: {fakePda} -> {assignment.server_ip}:{assignment.server_port}");
            _ = RefreshChallengesAsync();
        }
    }

    void AcceptTestChallenge(BackendClient.ChallengeInfo challenge)
    {
        if (_manaRef == null)
            _manaRef = FindFirstObjectByType<mana>();

        if (_manaRef == null)
        {
            Debug.LogError("[NetworkButtons] mana component not found — can't accept.");
            return;
        }

        if (!_manaRef.IsWalletConnected)
        {
            Debug.LogError("[NetworkButtons] Wallet not connected — can't accept.");
            return;
        }

        // IMPORTANT: accept must go through mana flow so it executes join_game on-chain.
        Debug.Log($"[NetworkButtons] Accept clicked for {challenge.game_pda[..8]}… — running on-chain join_game.");
        _manaRef.AcceptChallengeFromDebug(challenge);
        _ = RefreshChallengesAsync();
    }

    void ConnectToEditorServer()
    {
        if (_manaRef == null)
            _manaRef = FindFirstObjectByType<mana>();

        if (_manaRef != null && _manaRef.IsWalletConnected)
        {
            Debug.LogWarning("[NetworkButtons] Direct Connect disabled while wallet is connected. Use Create/Accept Challenge to run on-chain flow.");
            return;
        }

        if (_isConnecting) return;
        _isConnecting = true;

        TransportConfig.ApplyTransportConfig(force: true);

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.UseWebSockets = false;
        Time.fixedDeltaTime = 1f / 60f;

        if (Kart.Multiplayer.Instance != null)
        {
            Kart.Multiplayer.Instance.DirectConnect();
        }
        else
        {
            transport.SetConnectionData("127.0.0.1", 7777);
            NetworkManager.Singleton.StartClient();
        }

        Debug.Log("[NetworkButtons] Connecting to 127.0.0.1:7777...");
        _isConnecting = false;
    }

    string GetWalletStatus()
    {
        if (_manaRef == null) return "—";
        if (_manaRef.IsWalletConnected)
            return _manaRef.WalletPubkeyShort;
        return "Not connected";
    }

    static void EnsureEditorServerSystems()
    {
        if (FindFirstObjectByType<MatchManager>() == null)
        {
            var mmGo = new GameObject("MatchManager");
            mmGo.AddComponent<MatchManager>();
            DontDestroyOnLoad(mmGo);
            Debug.Log("[Editor] Created MatchManager runtime singleton.");
        }
    }

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
