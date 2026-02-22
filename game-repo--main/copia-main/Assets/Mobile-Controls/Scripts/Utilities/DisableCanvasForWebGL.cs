using UnityEngine;
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
using UnityEngine.InputSystem;
#endif

public class DisableCanvasForWebGL : MonoBehaviour
{
    void Awake()
    {
        #if UNITY_ANDROID || UNITY_IOS
            gameObject.SetActive(true);
        #else
            gameObject.SetActive(false);
        #endif
    }
}
