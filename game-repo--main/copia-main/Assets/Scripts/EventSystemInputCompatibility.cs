using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Keeps the scene EventSystem usable when the current Unity session is running
/// with the Input System package only. This prevents StandaloneInputModule from
/// throwing legacy-input exceptions every frame in stale editor/MPPM sessions.
/// </summary>
public static class EventSystemInputCompatibility
{
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Apply()
    {
        bool changedAny = false;
        var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var eventSystem in eventSystems)
        {
            var standalone = eventSystem.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                Object.Destroy(standalone);
                changedAny = true;
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                changedAny = true;
            }
        }

        if (changedAny)
        {
            Debug.Log("[EventSystemInputCompatibility] Legacy input is unavailable in this Unity session. Switched EventSystem to InputSystemUIInputModule.");
        }
    }
#endif
}
