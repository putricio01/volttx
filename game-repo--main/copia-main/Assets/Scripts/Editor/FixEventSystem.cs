#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem;

public static class FixEventSystem
{
    public static void Execute()
    {
        var es = GameObject.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (es == null)
        {
            Debug.LogError("No EventSystem found");
            return;
        }

        var module = es.GetComponent<InputSystemUIInputModule>();
        if (module == null)
        {
            Debug.LogError("No InputSystemUIInputModule found");
            return;
        }

        // Check if actionsAsset is already set
        if (module.actionsAsset != null)
        {
            Debug.Log($"[FixEventSystem] actionsAsset already set: {module.actionsAsset.name}");
            return;
        }

        // Create default UI input actions
        var asset = InputActionAsset.FromJson(@"{
            ""maps"": [
                {
                    ""name"": ""UI"",
                    ""actions"": [
                        { ""name"": ""Point"", ""type"": ""PassThrough"", ""expectedControlLayout"": ""Vector2"" },
                        { ""name"": ""Click"", ""type"": ""PassThrough"", ""expectedControlLayout"": ""Button"" },
                        { ""name"": ""ScrollWheel"", ""type"": ""PassThrough"", ""expectedControlLayout"": ""Vector2"" },
                        { ""name"": ""MiddleClick"", ""type"": ""PassThrough"", ""expectedControlLayout"": ""Button"" },
                        { ""name"": ""RightClick"", ""type"": ""PassThrough"", ""expectedControlLayout"": ""Button"" },
                        { ""name"": ""Navigate"", ""type"": ""PassThrough"", ""expectedControlLayout"": ""Vector2"" },
                        { ""name"": ""Submit"", ""type"": ""Button"" },
                        { ""name"": ""Cancel"", ""type"": ""Button"" }
                    ],
                    ""bindings"": [
                        { ""path"": ""<Mouse>/position"", ""action"": ""Point"" },
                        { ""path"": ""<Pen>/position"", ""action"": ""Point"" },
                        { ""path"": ""<Touchscreen>/touch*/position"", ""action"": ""Point"" },
                        { ""path"": ""<Mouse>/leftButton"", ""action"": ""Click"" },
                        { ""path"": ""<Pen>/tip"", ""action"": ""Click"" },
                        { ""path"": ""<Touchscreen>/touch*/press"", ""action"": ""Click"" },
                        { ""path"": ""<Mouse>/scroll"", ""action"": ""ScrollWheel"" },
                        { ""path"": ""<Mouse>/middleButton"", ""action"": ""MiddleClick"" },
                        { ""path"": ""<Mouse>/rightButton"", ""action"": ""RightClick"" },
                        { ""path"": ""<Keyboard>/escape"", ""action"": ""Cancel"" },
                        { ""path"": ""<Keyboard>/enter"", ""action"": ""Submit"" }
                    ]
                }
            ]
        }");

        // Save the asset
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");

        AssetDatabase.CreateAsset(asset, "Assets/Settings/DefaultUIInputActions.inputactions");
        AssetDatabase.SaveAssets();

        // Assign to module
        module.actionsAsset = asset;
        EditorUtility.SetDirty(module);
        EditorUtility.SetDirty(es.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("[FixEventSystem] Created and assigned DefaultUIInputActions to InputSystemUIInputModule");
    }
}
#endif
