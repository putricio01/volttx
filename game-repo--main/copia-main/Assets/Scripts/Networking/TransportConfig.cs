using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Configures the UnityTransport component on NetworkManager before any connections are made.
/// Uses multiple hooks to guarantee it runs in ALL scenarios:
/// - RuntimeInitializeOnLoadMethod (runs on every play mode start, including MPPM clones)
/// - Called explicitly before every StartServer/StartClient/StartHost call
///
/// Fixes "send queue full" / "Receive queue is full" disconnects.
/// </summary>
public static class TransportConfig
{
    // Large queue to handle RPC bursts in shared-process editor testing (MPPM)
    const int MAX_PACKET_QUEUE_SIZE = 1024;
    const int HEARTBEAT_TIMEOUT_MS = 2000;
    const int CONNECT_TIMEOUT_MS = 3000;

    static bool _applied = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        _applied = false;
        Application.quitting += Cleanup;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ConfigureAfterSceneLoad()
    {
        ApplyTransportConfig();
    }

    /// <summary>
    /// Apply transport settings. Safe to call multiple times — only applies once per session
    /// unless force=true. MUST be called before StartServer/StartClient.
    /// </summary>
    public static void ApplyTransportConfig(bool force = false)
    {
        if (_applied && !force) return;

        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[TransportConfig] NetworkManager.Singleton is null — will retry when available.");
            return;
        }

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogWarning("[TransportConfig] No UnityTransport found on NetworkManager.");
            return;
        }

        // Don't modify after connections are established
        if (NetworkManager.Singleton.IsListening)
        {
            Debug.Log("[TransportConfig] NetworkManager already listening, skipping.");
            return;
        }

        transport.MaxPacketQueueSize = MAX_PACKET_QUEUE_SIZE;
        transport.HeartbeatTimeoutMS = HEARTBEAT_TIMEOUT_MS;
        transport.ConnectTimeoutMS = CONNECT_TIMEOUT_MS;

        _applied = true;
        Debug.Log($"[TransportConfig] Applied: MaxPacketQueueSize={MAX_PACKET_QUEUE_SIZE}, " +
                  $"HeartbeatTimeout={HEARTBEAT_TIMEOUT_MS}ms, ConnectTimeout={CONNECT_TIMEOUT_MS}ms");
    }

    static void Cleanup()
    {
        _applied = false;
    }
}
