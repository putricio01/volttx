using UnityEngine;
using UnityEditor;
using Unity.Netcode;

public class AddCubeControllerToPrefabList
{
    public static void Execute()
    {
        // Find the "test" NetworkPrefabsList asset
        var guids = AssetDatabase.FindAssets("test t:NetworkPrefabsList");
        if (guids.Length == 0)
        {
            Debug.LogError("[ADD] Could not find NetworkPrefabsList named 'test'.");
            return;
        }

        string listPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        var prefabList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(listPath);
        if (prefabList == null)
        {
            Debug.LogError($"[ADD] Could not load NetworkPrefabsList at {listPath}.");
            return;
        }

        Debug.Log($"[ADD] Found NetworkPrefabsList at: {listPath}");

        // Find CubeController prefab
        var cubeGuids = AssetDatabase.FindAssets("CubeController t:Prefab");
        if (cubeGuids.Length == 0)
        {
            Debug.LogError("[ADD] Could not find CubeController prefab.");
            return;
        }

        string cubePath = AssetDatabase.GUIDToAssetPath(cubeGuids[0]);
        var cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cubePath);
        if (cubePrefab == null)
        {
            Debug.LogError($"[ADD] Could not load CubeController prefab at {cubePath}.");
            return;
        }

        Debug.Log($"[ADD] Found CubeController prefab at: {cubePath}");

        // Check if already in list
        foreach (var p in prefabList.PrefabList)
        {
            if (p.Prefab == cubePrefab)
            {
                Debug.Log("[ADD] CubeController is already in the list!");
                return;
            }
        }

        // Add it using SerializedObject for proper undo/save
        var so = new SerializedObject(prefabList);
        var listProp = so.FindProperty("List");
        int newIndex = listProp.arraySize;
        listProp.InsertArrayElementAtIndex(newIndex);
        var newElement = listProp.GetArrayElementAtIndex(newIndex);
        var prefabProp = newElement.FindPropertyRelative("Prefab");
        prefabProp.objectReferenceValue = cubePrefab;
        // Clear override fields
        var overrideProp = newElement.FindPropertyRelative("Override");
        if (overrideProp != null) overrideProp.enumValueIndex = 0;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(prefabList);
        AssetDatabase.SaveAssets();

        Debug.Log($"[ADD] Successfully added CubeController to NetworkPrefabsList 'test'!");
        Debug.Log($"[ADD] List now has {prefabList.PrefabList.Count} prefabs.");
    }
}
