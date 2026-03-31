using UnityEngine;

/// <summary>
/// Central configuration for VoltTx game.
/// Create an instance via Assets → Create → VoltTx → GameConfig
/// and place it at Resources/GameConfig.
/// </summary>
[CreateAssetMenu(fileName = "GameConfig", menuName = "VoltTx/GameConfig")]
public class GameConfig : ScriptableObject
{
    [Header("Solana")]
    [Tooltip("Server authority pubkey (base58). Must match AUTHORITY_PUBKEY in backend .env.")]
    public string authorityPubkey;

    [Tooltip("On-chain program ID (base58).")]
    public string programId = "3abFWCLDDyA2jHfnGLQUTX6W9jddXSMHt9jtyc6Xjfjc";

    [Header("Backend")]
    [Tooltip("Base URL for the Rust backend REST API.")]
    public string backendUrl = "http://127.0.0.1:8000";

    [Tooltip("Shared secret used by server->backend internal HMAC auth (/v1/finalize). Leave empty only if provided via env var INTERNAL_HMAC_SECRET.")]
    public string internalHmacSecret = "";

    [Header("Direct Connect")]
    [Tooltip("Editor/desktop direct-connect target. Keep localhost for same-machine MPPM tests.")]
    public string editorDirectConnectIp = "127.0.0.1";

    [Tooltip("LAN/VPS IP used by Android/iPhone test builds for direct-connect debugging.")]
    public string externalDirectConnectIp = "127.0.0.1";

    [Tooltip("Port used by direct-connect debugging.")]
    public int directConnectPort = 7777;

    [Tooltip("Shows the runtime direct-connect overlay in non-editor debug builds.")]
    public bool showRuntimeDirectConnectOverlay = true;

    [Tooltip("When true, direct-connect clients without wallet/challenge flow auto-submit dev metadata so the match can start.")]
    public bool autoSubmitDirectConnectDevMetadata = true;

    [Tooltip("Shared fake game PDA used by direct-connect debug clients when no wallet/challenge flow is active.")]
    public string directConnectDevGamePda = "lan-dev-match";

    [Tooltip("How long a direct-connect client waits for its local PlayerObject before logging a warning.")]
    public float directConnectPlayerSpawnTimeoutSeconds = 4f;

    public string GetDirectConnectIp()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return string.IsNullOrWhiteSpace(editorDirectConnectIp) ? "127.0.0.1" : editorDirectConnectIp.Trim();
#else
        if (!string.IsNullOrWhiteSpace(externalDirectConnectIp))
            return externalDirectConnectIp.Trim();

        return string.IsNullOrWhiteSpace(editorDirectConnectIp) ? "127.0.0.1" : editorDirectConnectIp.Trim();
#endif
    }

    public ushort GetDirectConnectPort()
    {
        return (ushort)Mathf.Clamp(directConnectPort, 1, 65535);
    }

    public string GetDirectConnectDevGamePda()
    {
        return string.IsNullOrWhiteSpace(directConnectDevGamePda) ? "lan-dev-match" : directConnectDevGamePda.Trim();
    }

    public float GetDirectConnectPlayerSpawnTimeoutSeconds()
    {
        return Mathf.Max(1f, directConnectPlayerSpawnTimeoutSeconds);
    }

    /// <summary>
    /// Load the singleton GameConfig from Resources/GameConfig.
    /// </summary>
    public static GameConfig Load()
    {
        var cfg = Resources.Load<GameConfig>("GameConfig");
        if (cfg == null)
            Debug.LogError("[GameConfig] Missing Resources/GameConfig asset. Create one via Assets → Create → VoltTx → GameConfig.");
        return cfg;
    }
}
