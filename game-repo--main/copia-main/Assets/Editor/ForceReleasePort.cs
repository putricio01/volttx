using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class ForceReleasePort
{
    public static void Execute()
    {
        Debug.Log("[PORT] Attempting to force release port 7777...");

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsListening)
            {
                Debug.Log("[PORT] NetworkManager is listening, shutting down...");
                nm.Shutdown(true);
                Debug.Log("[PORT] NetworkManager shutdown called.");
            }

            var transport = nm.GetComponent<UnityTransport>();
            if (transport != null)
            {
                // Force shutdown the transport
                Debug.Log("[PORT] Calling transport.Shutdown()...");
                transport.Shutdown();
                Debug.Log("[PORT] Transport shutdown called.");
            }
        }

        // Also try to find any other NetworkManager instances
        var allNMs = Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
        Debug.Log($"[PORT] Found {allNMs.Length} NetworkManager instances.");
        foreach (var n in allNMs)
        {
            if (n.IsListening)
            {
                Debug.Log($"[PORT] Extra NM on '{n.gameObject.name}' is listening, shutting down...");
                n.Shutdown(true);
            }
            var t = n.GetComponent<UnityTransport>();
            if (t != null)
            {
                t.Shutdown();
            }
        }

        Debug.Log("[PORT] Done. Try starting the server again.");
    }
}
