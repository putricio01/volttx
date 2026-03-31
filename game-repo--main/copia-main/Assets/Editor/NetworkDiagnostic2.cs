using UnityEngine;
using UnityEditor;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class NetworkDiagnostic2
{
    public static void Execute()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.Log("[DIAG2] NetworkManager.Singleton is NULL");
            return;
        }

        Debug.Log($"[DIAG2] === Network Diagnostic ===");
        Debug.Log($"[DIAG2] IsPlaying={Application.isPlaying}");
        Debug.Log($"[DIAG2] IsServer={nm.IsServer} IsClient={nm.IsClient} IsHost={nm.IsHost} IsListening={nm.IsListening}");
        Debug.Log($"[DIAG2] ConnectedClients={nm.ConnectedClients?.Count ?? 0}");

        if (nm.IsServer)
        {
            Debug.Log($"[DIAG2] Server IS running!");
            foreach (var kvp in nm.ConnectedClients)
            {
                var po = kvp.Value.PlayerObject;
                Debug.Log($"[DIAG2]   Client {kvp.Key}: PlayerObject={(po != null ? po.name : "NULL")}");
            }
        }
        else
        {
            Debug.Log($"[DIAG2] Server is NOT running on this instance.");
        }

        var transport = nm.GetComponent<UnityTransport>();
        if (transport != null)
        {
            Debug.Log($"[DIAG2] Transport: UseWebSockets={transport.UseWebSockets}");
            Debug.Log($"[DIAG2] Transport: HeartbeatTimeout={transport.HeartbeatTimeoutMS}ms, ConnectTimeout={transport.ConnectTimeoutMS}ms");
            Debug.Log($"[DIAG2] Transport: MaxPacketQueueSize={transport.MaxPacketQueueSize}");
            var connData = transport.ConnectionData;
            Debug.Log($"[DIAG2] Transport: Address={connData.Address}, Port={connData.Port}, ServerListenAddress={connData.ServerListenAddress}");
        }

        // Check player prefab
        var prefab = nm.NetworkConfig?.PlayerPrefab;
        Debug.Log($"[DIAG2] PlayerPrefab={(prefab != null ? prefab.name : "NULL")}");

        if (prefab != null)
        {
            var no = prefab.GetComponent<NetworkObject>();
            Debug.Log($"[DIAG2] PlayerPrefab has NetworkObject={no != null}");

            var components = prefab.GetComponents<MonoBehaviour>();
            Debug.Log($"[DIAG2] PlayerPrefab components ({components.Length}):");
            foreach (var c in components)
            {
                Debug.Log($"[DIAG2]   - {c.GetType().Name} (enabled={c.enabled})");
            }
        }

        // Check for MatchManager
        var mm = Object.FindFirstObjectByType<MatchManager>();
        Debug.Log($"[DIAG2] MatchManager={(mm != null ? $"found, state={mm.CurrentState}" : "NOT FOUND")}");

        // Check for ServerPlayerObjectFallbackSpawner
        var spawner = Object.FindFirstObjectByType<ServerPlayerObjectFallbackSpawner>();
        Debug.Log($"[DIAG2] FallbackSpawner={(spawner != null ? "found" : "NOT FOUND")}");

        // Check for DirectConnectDevSession
        var devSession = Object.FindFirstObjectByType<DirectConnectDevSession>();
        Debug.Log($"[DIAG2] DevSession={(devSession != null ? "found" : "NOT FOUND")}");

        // Check network prefabs list
        var prefabs = nm.NetworkConfig?.Prefabs;
        if (prefabs?.NetworkPrefabsLists != null)
        {
            foreach (var list in prefabs.NetworkPrefabsLists)
            {
                Debug.Log($"[DIAG2] NetworkPrefabList '{list.name}': {list.PrefabList.Count} prefabs");
                foreach (var p in list.PrefabList)
                {
                    if (p.Prefab != null)
                    {
                        var hasNO = p.Prefab.GetComponent<NetworkObject>() != null;
                        Debug.Log($"[DIAG2]   - {p.Prefab.name} (hasNetworkObject={hasNO})");
                    }
                }
            }
        }

        Debug.Log($"[DIAG2] === End Diagnostic ===");
    }
}
