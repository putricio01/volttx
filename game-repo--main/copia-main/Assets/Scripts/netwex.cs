using UnityEngine;
using Unity.Netcode;
using Cinemachine;

/// <summary>
/// Network spawn handler. Sets up Cinemachine camera priority based on ownership.
/// Spawn point assignment is now handled by PlayerRespawner (server-side).
/// </summary>
public class netwex : NetworkBehaviour
{
    [Header("Owner Camera")]
    [SerializeField] public CinemachineFreeLook vcc;
    [SerializeField] bool useFixedOwnerCamera = true;
    [SerializeField] bool lockOwnerCameraBehindTarget = true;
    [SerializeField, Range(0f, 1f)] float lockedVerticalAxis = 0.5f;
    [SerializeField, Min(0f)] float headingRecenteringWaitTime = 0f;
    [SerializeField, Min(0.01f)] float headingRecenteringTime = 0.2f;
    public Transform[] spawnPoints;

    Camera _mountedCamera;
    CinemachineBrain _cameraBrain;

    public bool LockOwnerCameraBehindTarget => lockOwnerCameraBehindTarget;

    void Awake()
    {
        CacheCameraRefs();
    }

    public override void OnNetworkSpawn()
    {
        CacheCameraRefs();

        // Server doesn't need cameras at all
        if (IsServer && !IsHost)
        {
            if (vcc != null) vcc.gameObject.SetActive(false);
            return;
        }

        if (vcc == null) return;

        if (IsOwner)
        {
            if (useFixedOwnerCamera)
            {
                ConfigureFixedOwnerCamera();
            }
            else
            {
                ConfigureFreeLookOwnerCamera();
            }
        }
        else
        {
            ConfigureRemoteCamera();
        }

        // Note: Spawn point assignment is now handled server-side by PlayerRespawner.
        // This script only manages client-side camera priority.
    }

    void CacheCameraRefs()
    {
        if (_mountedCamera == null)
            _mountedCamera = GetComponentInChildren<Camera>(true);

        if (_cameraBrain == null)
            _cameraBrain = GetComponentInChildren<CinemachineBrain>(true);
    }

    void ConfigureFixedOwnerCamera()
    {
        if (vcc != null)
        {
            vcc.Priority = 0;
            vcc.gameObject.SetActive(false);
        }

        if (_cameraBrain != null)
            _cameraBrain.enabled = false;

        if (_mountedCamera != null)
            _mountedCamera.enabled = true;
    }

    void ConfigureFreeLookOwnerCamera()
    {
        if (_mountedCamera != null)
            _mountedCamera.enabled = true;

        if (_cameraBrain != null)
            _cameraBrain.enabled = true;

        if (vcc == null)
            return;

        vcc.gameObject.SetActive(true);
        vcc.Priority = 1;
        ConfigureOwnerCamera();
    }

    void ConfigureRemoteCamera()
    {
        if (vcc != null)
            vcc.Priority = 0;

        if (!IsServer)
        {
            if (_cameraBrain != null)
                _cameraBrain.enabled = false;

            if (_mountedCamera != null)
                _mountedCamera.enabled = false;
        }
    }

    void ConfigureOwnerCamera()
    {
        if (vcc == null || !lockOwnerCameraBehindTarget)
            return;

        vcc.m_XAxis.m_InputAxisName = string.Empty;
        vcc.m_YAxis.m_InputAxisName = string.Empty;
        vcc.m_XAxis.Value = 0f;
        vcc.m_YAxis.Value = lockedVerticalAxis;
        vcc.m_XAxis.m_InputAxisValue = 0f;
        vcc.m_YAxis.m_InputAxisValue = 0f;
        vcc.m_YAxis.m_MaxSpeed = 0f;

        var yRecentering = vcc.m_YAxisRecentering;
        yRecentering.m_enabled = false;
        vcc.m_YAxisRecentering = yRecentering;

        var recenter = vcc.m_RecenterToTargetHeading;
        recenter.m_enabled = true;
        recenter.m_WaitTime = headingRecenteringWaitTime;
        recenter.m_RecenteringTime = headingRecenteringTime;
        vcc.m_RecenterToTargetHeading = recenter;

        ToggleManualCameraInput(false);
    }

    void ToggleManualCameraInput(bool enabled)
    {
        var inputSwitcher = vcc.GetComponent<CinemachineInputSwitcher>();
        if (inputSwitcher != null)
            inputSwitcher.enabled = enabled;

        var inputManager = vcc.GetComponent<CinemachineInputManager>();
        if (inputManager != null)
            inputManager.enabled = enabled;
    }
}
