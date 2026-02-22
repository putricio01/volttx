using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time fix: Sets Active Input Handling to "Both" so legacy Input API works.
/// </summary>
public class FixInputHandler
{
    public static void Execute()
    {
        // PlayerSettings stores the active input handler
        // 0 = Both, 1 = Input Manager (Old), 2 = Input System Package (New)
        var playerSettings = Resources.FindObjectsOfTypeAll<PlayerSettings>();

        var so = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]
        );
        var prop = so.FindProperty("activeInputHandler");

        if (prop != null)
        {
            Debug.Log($"[FixInputHandler] Current activeInputHandler = {prop.intValue}");
            prop.intValue = 0; // Both
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[FixInputHandler] Set activeInputHandler to 0 (Both). RESTART UNITY for it to take effect.");
        }
        else
        {
            Debug.LogError("[FixInputHandler] Could not find activeInputHandler property!");
        }
    }
}
