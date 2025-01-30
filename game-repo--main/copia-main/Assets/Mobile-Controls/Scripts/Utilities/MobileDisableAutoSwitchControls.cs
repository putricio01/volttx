using UnityEngine;
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
using UnityEngine.InputSystem;
#endif

public class MobileDisableAutoSwitchControls : MonoBehaviour
{
    #if ENABLE_INPUT_SYSTEM && (UNITY_IOS || UNITY_ANDROID) && STARTER_ASSETS_PACKAGES_CHECKED
    [Header("Target")]
    public PlayerInput playerInput;

    void Start()
    {
        DisableAutoSwitchControls();
    }

    void DisableAutoSwitchControls()
    {
        playerInput.neverAutoSwitchControlSchemes = true;
    }
    #endif
}

