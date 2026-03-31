using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// For direct-connect debug sessions (no wallet/challenge flow), auto-submits
/// fake player metadata once the local PlayerObject exists so MatchManager can
/// advance to WaitingForReady/InProgress with two test clients.
/// Also logs when a client connects but never receives its PlayerObject.
/// </summary>
public class DirectConnectDevSession : MonoBehaviour
{
    const string k_playerPrefsKey = "DirectConnectDevSession.DeviceId";

    static DirectConnectDevSession _instance;

    float _connectedAt = -1f;
    bool _loggedWaitingForPlayer;
    bool _submittedDevMetadata;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null || FindFirstObjectByType<DirectConnectDevSession>() != null)
            return;

        var go = new GameObject(nameof(DirectConnectDevSession));
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<DirectConnectDevSession>();
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
        if (nm == null || !nm.IsClient || nm.IsServer)
        {
            ResetSessionState();
            return;
        }

        if (!nm.IsConnectedClient)
        {
            ResetSessionState();
            return;
        }

        if (_connectedAt < 0f)
        {
            _connectedAt = Time.unscaledTime;
        }

        var localPlayerObject = nm.SpawnManager?.GetLocalPlayerObject();
        if (localPlayerObject == null)
        {
            MaybeLogMissingPlayerObject();
            return;
        }

        if (ShouldUseWalletFlow())
            return;

        if (_submittedDevMetadata)
            return;

        var lobbyNet = localPlayerObject.GetComponent<PlayerLobbyNet>();
        if (lobbyNet == null)
        {
            Debug.LogWarning("[DirectConnectDevSession] Local PlayerObject spawned, but PlayerLobbyNet is missing.");
            return;
        }

        var cfg = GameConfig.Load();
        if (cfg == null || !cfg.autoSubmitDirectConnectDevMetadata)
            return;

        string wallet = GetOrCreateDevWalletId();
        string gamePda = cfg.GetDirectConnectDevGamePda();

        lobbyNet.SubmitPlayerInfoServerRpc(wallet, gamePda);
        lobbyNet.SubmitReadyServerRpc();
        _submittedDevMetadata = true;

        Debug.Log($"[DirectConnectDevSession] Submitted dev player info. wallet={wallet}, game={gamePda}");
    }

    void MaybeLogMissingPlayerObject()
    {
        if (_loggedWaitingForPlayer)
            return;

        var cfg = GameConfig.Load();
        float timeout = cfg != null ? cfg.GetDirectConnectPlayerSpawnTimeoutSeconds() : 4f;
        if (Time.unscaledTime - _connectedAt < timeout)
            return;

        _loggedWaitingForPlayer = true;
        Debug.LogError("[DirectConnectDevSession] Connected as client, but LocalPlayerObject never spawned. Camera/controls will stay unavailable until the player prefab exists.");
    }

    static bool ShouldUseWalletFlow()
    {
#if UNITY_SERVER
        return false;
#else
        var lobby = FindFirstObjectByType<mana>();
        return lobby != null && lobby.IsWalletConnected;
#endif
    }

    static string GetOrCreateDevWalletId()
    {
        string existing = PlayerPrefs.GetString(k_playerPrefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(existing))
            return $"devwallet_{existing}";

        string created = Guid.NewGuid().ToString("N")[..16];
        PlayerPrefs.SetString(k_playerPrefsKey, created);
        PlayerPrefs.Save();
        return $"devwallet_{created}";
    }

    void ResetSessionState()
    {
        _connectedAt = -1f;
        _loggedWaitingForPlayer = false;
        _submittedDevMetadata = false;
    }
}
