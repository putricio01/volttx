using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class ResetTransport
{
    public static void Execute()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.Log("[RESET] No NetworkManager found");
            return;
        }

        if (nm.IsListening)
        {
            Debug.Log("[RESET] NetworkManager is listening, shutting down...");
            nm.Shutdown();
            Debug.Log("[RESET] Shutdown complete.");
        }
        else
        {
            Debug.Log("[RESET] NetworkManager is NOT listening. Port may be stuck from previous session.");
        }

        var transport = nm.GetComponent<UnityTransport>();
        if (transport != null)
        {
            Debug.Log("[RESET] Transport found. Current state logged.");
        }
    }
}
