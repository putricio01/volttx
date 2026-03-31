using UnityEngine;
using UnityEditor;
using Unity.Netcode;

public class CheckNetworkManager
{
    public static void Execute()
    {
        var nm = Object.FindFirstObjectByType<NetworkManager>();
        if (nm == null)
        {
            Debug.Log("[CHECK] NetworkManager NOT FOUND in scene");
            return;
        }

        Debug.Log($"[CHECK] NetworkManager found on '{nm.gameObject.name}'");

        if (nm.NetworkConfig == null)
        {
            Debug.Log("[CHECK] NetworkConfig is NULL");
            return;
        }

        var prefab = nm.NetworkConfig.PlayerPrefab;
        Debug.Log($"[CHECK] PlayerPrefab = {(prefab != null ? prefab.name : "NULL / NOT ASSIGNED")}");

        Debug.Log($"[CHECK] ConnectionApproval = {nm.NetworkConfig.ConnectionApproval}");

        var prefabs = nm.NetworkConfig.Prefabs;
        if (prefabs != null && prefabs.NetworkPrefabsLists != null)
        {
            foreach (var list in prefabs.NetworkPrefabsLists)
            {
                Debug.Log($"[CHECK] PrefabList: {list.name} with {list.PrefabList.Count} prefabs");
                foreach (var p in list.PrefabList)
                {
                    if (p.Prefab != null)
                        Debug.Log($"[CHECK]   - {p.Prefab.name}");
                }
            }
        }

        Debug.Log($"[CHECK] IsServer={nm.IsServer} IsClient={nm.IsClient} IsHost={nm.IsHost} IsListening={nm.IsListening}");
    }
}
