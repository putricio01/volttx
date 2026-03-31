#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class AddWalletController
{
    public static void Execute()
    {
        // Check if [WalletController] already exists in scene
        var existing = GameObject.Find("[WalletController]");
        if (existing != null)
        {
            Debug.Log("[AddWalletController] Already in scene.");
            return;
        }

        // Load from Resources
        var prefab = Resources.Load<GameObject>("SolanaUnitySDK/[WalletController]");
        if (prefab == null)
        {
            Debug.LogError("[AddWalletController] [WalletController] prefab not found in Resources/SolanaUnitySDK/");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "[WalletController]";

        EditorUtility.SetDirty(instance);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("[AddWalletController] Added [WalletController] to scene.");
    }
}
#endif
