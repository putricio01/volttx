using Unity.Netcode;
using UnityEngine;
using Cinemachine;

public class MobileControlsBootstrap : MonoBehaviour
{
    [SerializeField] bool enableInEditor;
    [SerializeField, Range(30, 120)] int mobileTargetFrameRate = 60;
    [SerializeField, Range(0.02f, 0.2f)] float maxFrameDelta = 0.1f;
    [Header("Mobile Performance")]
    [SerializeField] bool applyMobilePerformanceProfile = true;
    [SerializeField, Range(0.5f, 1f)] float initialRenderScale = 0.8f;
    [SerializeField, Range(0.5f, 1f)] float minimumRenderScale = 0.65f;
    [SerializeField, Range(0.05f, 0.2f)] float renderScaleStep = 0.1f;
    [SerializeField, Range(20f, 60f)] float adaptiveFpsThreshold = 28f;
    [SerializeField, Range(0.25f, 5f)] float adaptiveWindowSeconds = 1.5f;
    [SerializeField, Range(0, 3)] int textureMipmapLimit = 1;
    [SerializeField] bool disableShadows = true;
    [SerializeField] bool disableHdrAndMsaa = true;

    UICanvasControllerInput _canvasInput;
    InputManager _boundInputManager;
    MobileCameraTouch _cameraTouch;
    bool _lookJoystickRemoved;
    bool _runtimeTuningApplied;
    float _currentRenderScale = 1f;
    float _adaptiveFpsTime;
    float _adaptiveFpsAccum;
    int _adaptiveFpsSamples;
    int _lastConfiguredCameraCount = -1;

    public float DebugRenderScale => _currentRenderScale;
    public int DebugQualityLevel => QualitySettings.GetQualityLevel();

    bool ShouldUseMobileControls()
    {
#if UNITY_ANDROID || UNITY_IOS
        return true;
#elif UNITY_EDITOR
        return enableInEditor;
#else
        return false;
#endif
    }

    void Awake()
    {
        ApplyRuntimeTuning();
    }

    void Update()
    {
        if (!ShouldUseMobileControls()) return;

        ApplyCameraPerformanceSettings();
        UpdateAdaptivePerformance();

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            UnbindInputManager();
            return;
        }

        var localPlayerObject = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
        if (localPlayerObject == null)
        {
            UnbindInputManager();
            return;
        }

        var inputManager = localPlayerObject.GetComponent<InputManager>();
        if (inputManager == null) return;

        // Find the canvas that's already on the CubeController
        if (_canvasInput == null)
        {
            _canvasInput = localPlayerObject.GetComponentInChildren<UICanvasControllerInput>(true);
            if (_canvasInput == null) return;
            Debug.Log("[MobileControls] Found existing canvas on player object.");
        }

        // Bind input manager
        if (_boundInputManager != inputManager)
        {
            _canvasInput.inputManager = inputManager;
            _boundInputManager = inputManager;
            Debug.Log("[MobileControls] Bound InputManager to canvas.");
        }

        // Remove look joystick once (camera handled by touch)
        if (!_lookJoystickRemoved)
        {
            RemoveLookJoystick(_canvasInput.gameObject);
            _lookJoystickRemoved = true;
        }

        if (ShouldLockOwnerCameraBehindTarget(localPlayerObject))
        {
            DisableCameraTouch();
        }
        else
        {
            EnsureCameraTouch();
        }
    }

    void ApplyRuntimeTuning()
    {
        if (_runtimeTuningApplied || !ShouldUseMobileControls()) return;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = mobileTargetFrameRate;
        Time.maximumDeltaTime = maxFrameDelta;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        if (applyMobilePerformanceProfile)
        {
            QualitySettings.pixelLightCount = 0;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias, 0.6f);
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            QualitySettings.globalTextureMipmapLimit = Mathf.Max(QualitySettings.globalTextureMipmapLimit, textureMipmapLimit);

            if (disableShadows)
            {
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.shadowDistance = 0f;
            }

            SetRenderScale(initialRenderScale);
            ApplyCameraPerformanceSettings(force: true);
        }

        _runtimeTuningApplied = true;
        Debug.Log($"[MobileControls] Runtime tuning applied. targetFrameRate={mobileTargetFrameRate} maxDelta={maxFrameDelta:0.000} renderScale={_currentRenderScale:0.00} shadows={(QualitySettings.shadows == ShadowQuality.Disable ? "off" : "on")}");
    }

    void UpdateAdaptivePerformance()
    {
        if (!_runtimeTuningApplied || !applyMobilePerformanceProfile)
            return;

        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        _adaptiveFpsAccum += 1f / dt;
        _adaptiveFpsTime += dt;
        _adaptiveFpsSamples++;

        if (_adaptiveFpsTime < adaptiveWindowSeconds || _adaptiveFpsSamples <= 0)
            return;

        float averageFps = _adaptiveFpsAccum / _adaptiveFpsSamples;
        _adaptiveFpsAccum = 0f;
        _adaptiveFpsTime = 0f;
        _adaptiveFpsSamples = 0;

        if (averageFps >= adaptiveFpsThreshold || _currentRenderScale <= minimumRenderScale + 0.001f)
            return;

        float nextScale = Mathf.Max(minimumRenderScale, _currentRenderScale - renderScaleStep);
        if (nextScale < _currentRenderScale - 0.001f)
        {
            SetRenderScale(nextScale);
            Debug.Log($"[MobileControls] Adaptive render scale lowered to {_currentRenderScale:0.00} after average FPS {averageFps:0.0}");
        }
    }

    void SetRenderScale(float scale)
    {
        _currentRenderScale = Mathf.Clamp(scale, minimumRenderScale, 1f);
        ScalableBufferManager.ResizeBuffers(_currentRenderScale, _currentRenderScale);
    }

    void ApplyCameraPerformanceSettings(bool force = false)
    {
        if (!disableHdrAndMsaa)
            return;

        int currentCameraCount = Camera.allCamerasCount;
        if (!force && currentCameraCount == _lastConfiguredCameraCount)
            return;

        _lastConfiguredCameraCount = currentCameraCount;

        foreach (var cam in Camera.allCameras)
        {
            if (cam == null) continue;
            cam.allowHDR = false;
            cam.allowMSAA = false;
        }
    }

    void EnsureCameraTouch()
    {
        if (_cameraTouch == null)
        {
            _cameraTouch = gameObject.GetComponent<MobileCameraTouch>();
            if (_cameraTouch == null)
            {
                _cameraTouch = gameObject.AddComponent<MobileCameraTouch>();
                Debug.Log("[MobileControls] Added MobileCameraTouch.");
            }
        }

        if (!_cameraTouch.enabled)
        {
            _cameraTouch.enabled = true;
        }

        if (_cameraTouch.freeLookCamera == null)
        {
            var freeLook = FindObjectOfType<CinemachineFreeLook>();
            if (freeLook != null)
            {
                _cameraTouch.freeLookCamera = freeLook;
                Debug.Log("[MobileControls] Wired CinemachineFreeLook to camera touch.");
            }
        }
    }

    bool ShouldLockOwnerCameraBehindTarget(NetworkObject localPlayerObject)
    {
        if (localPlayerObject == null) return false;

        var cameraOwner = localPlayerObject.GetComponent<netwex>();
        return cameraOwner != null && cameraOwner.LockOwnerCameraBehindTarget;
    }

    void DisableCameraTouch()
    {
        if (_cameraTouch == null) return;

        _cameraTouch.freeLookCamera = null;
        _cameraTouch.enabled = false;
    }

    void RemoveLookJoystick(GameObject canvasRoot)
    {
        if (canvasRoot == null) return;

        var allTransforms = canvasRoot.GetComponentsInChildren<Transform>(true);
        foreach (var t in allTransforms)
        {
            if (t != null && (t.name.Contains("Look") || t.name.Contains("look")))
            {
                Debug.Log($"[MobileControls] Removing look joystick: {t.name}");
                Destroy(t.gameObject);
            }
        }
    }

    void UnbindInputManager()
    {
        _boundInputManager = null;
        if (_canvasInput != null)
        {
            _canvasInput.inputManager = null;
        }
    }
}
