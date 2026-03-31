using UnityEngine;
using UnityEditor;
using Unity.Netcode;

public class FixNetworkPrefabs
{
    public static void Execute()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            nm = Object.FindFirstObjectByType<NetworkManager>();

        if (nm == null)
        {
            Debug.LogError("[FIX] No NetworkManager found.");
            return;
        }

        var playerPrefab = nm.NetworkConfig?.PlayerPrefab;
        if (playerPrefab == null)
        {
            Debug.LogError("[FIX] PlayerPrefab is null.");
            return;
        }

        Debug.Log($"[FIX] PlayerPrefab = {playerPrefab.name}");

        // Check if PlayerPrefab is in any NetworkPrefabsList
        bool found = false;
        var prefabs = nm.NetworkConfig.Prefabs;
        if (prefabs?.NetworkPrefabsLists != null)
        {
            foreach (var list in prefabs.NetworkPrefabsLists)
            {
                foreach (var p in list.PrefabList)
                {
                    if (p.Prefab == playerPrefab)
                    {
                        found = true;
                        Debug.Log($"[FIX] PlayerPrefab '{playerPrefab.name}' FOUND in list '{list.name}'.");
                        break;
                    }
                }
                if (found) break;
            }
        }

        if (!found)
        {
            Debug.LogWarning($"[FIX] PlayerPrefab '{playerPrefab.name}' is NOT in any NetworkPrefabsList! This will cause spawn failures on clients.");
            Debug.LogWarning("[FIX] Please add CubeController to the Network Prefabs list in NetworkManager Inspector.");
        }
        else
        {
            Debug.Log("[FIX] PlayerPrefab registration looks correct.");
        }

        Debug.Log($"[FIX] Done.");
    }
}
