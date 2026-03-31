#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class CreateGameConfigAsset
{
    public static void Execute()
    {
        // Ensure Resources folder exists
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        // Create the ScriptableObject instance
        var config = ScriptableObject.CreateInstance<GameConfig>();
        config.authorityPubkey = "";
        config.programId = "3abFWCLDDyA2jHfnGLQUTX6W9jddXSMHt9jtyc6Xjfjc";
        config.backendUrl = "http://127.0.0.1:8000";

        // Save as asset
        AssetDatabase.CreateAsset(config, "Assets/Resources/GameConfig.asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[CreateGameConfigAsset] Created Assets/Resources/GameConfig.asset");
    }
}
#endif
