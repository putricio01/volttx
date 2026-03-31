using UnityEngine;
using Cinemachine;

/// <summary>
/// Controls Cinemachine FreeLook camera via touch on the right side of the screen.
/// Any touch on the right 60% of the screen that isn't on a UI element will orbit the camera.
/// Attach to any persistent GameObject (e.g. MobileControlsBootstrap).
/// </summary>
public class MobileCameraTouch : MonoBehaviour
{
    [Header("Camera")]
    public CinemachineFreeLook freeLookCamera;

    [Header("Sensitivity")]
    public float xSensitivity = 0.15f;
    public float ySensitivity = 0.004f;

    [Header("Screen Zone")]
    [Tooltip("Touch must start in the right portion of the screen (0.4 = right 60%)")]
    public float screenLeftCutoff = 0.4f;

    // Normalize pixel deltas so the same physical swipe feels identical across DPIs
    const float ReferenceDPI = 400f;

    int _activeFingerId = -1;
    Vector2 _lastTouchPos;
    float _dpiScale = 1f;

    void OnEnable()
    {
        _dpiScale = Screen.dpi > 0f ? ReferenceDPI / Screen.dpi : 1f;

        if (freeLookCamera != null)
        {
            // Disable default mouse input so we control it manually
            freeLookCamera.m_XAxis.m_InputAxisName = "";
            freeLookCamera.m_YAxis.m_InputAxisName = "";
        }
    }

    void Update()
    {
        if (freeLookCamera == null) return;

#if UNITY_ANDROID || UNITY_IOS
        HandleTouchInput();
#endif
    }

    void HandleTouchInput()
    {
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);

            if (touch.phase == TouchPhase.Began)
            {
                // Only accept touches on the right side of screen and not already tracking
                if (_activeFingerId < 0 && !IsOverUI(touch.fingerId))
                {
                    float normalizedX = touch.position.x / Screen.width;
                    if (normalizedX > screenLeftCutoff)
                    {
                        _activeFingerId = touch.fingerId;
                        _lastTouchPos = touch.position;
                    }
                }
            }
            else if (touch.fingerId == _activeFingerId)
            {
                if (touch.phase == TouchPhase.Moved)
                {
                    Vector2 delta = (touch.position - _lastTouchPos) * _dpiScale;
                    _lastTouchPos = touch.position;

                    freeLookCamera.m_XAxis.Value += delta.x * xSensitivity;
                    freeLookCamera.m_YAxis.Value -= delta.y * ySensitivity;
                    freeLookCamera.m_YAxis.Value = Mathf.Clamp(freeLookCamera.m_YAxis.Value, 0f, 1f);
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    _activeFingerId = -1;
                }
            }
        }
    }

    static bool IsOverUI(int fingerId)
    {
        return UnityEngine.EventSystems.EventSystem.current != null &&
               UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(fingerId);
    }
}
