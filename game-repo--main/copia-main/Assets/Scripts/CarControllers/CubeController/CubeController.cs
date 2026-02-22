using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class CubeController : NetworkBehaviour
{
    PlayerRespawner playerRespawner;

    [Header("score")]
    public int score;

    [Header("Car State")]
    public bool isAllWheelsSurface = false;
    public bool isCanDrive;
    public float forwardSpeed = 0, forwardSpeedSign = 0, forwardSpeedAbs = 0;
    public int numWheelsSurface;
    public bool isBodySurface;
    public CarStates carState;

    [Header("Other")]
    public Transform cogLow;
    public GameObject sceneViewFocusObject;

    public const float MaxSpeedBoost = 2300 / 100;

    Rigidbody _rb;
    GUIStyle _style;
    CubeSphereCollider[] _sphereColliders;

    public enum CarStates
    {
        AllWheelsGround,
        Air,
        AllWheelsSurface,
        SomeWheelsSurface,
        BodySideGround,
        BodyGroundDead
    }

    void Start()
    {
        playerRespawner = FindObjectOfType<PlayerRespawner>();
        playerRespawner.Respawns.Insert((int)NetworkManager.LocalClientId, gameObject);

        _rb = GetComponent<Rigidbody>();
        _rb.centerOfMass = cogLow.localPosition;
        _rb.maxAngularVelocity = 5.5f;

        _sphereColliders = GetComponentsInChildren<CubeSphereCollider>();

        // GUI stuff
        _style = new GUIStyle();
        _style.normal.textColor = Color.red;
        _style.fontSize = 25;
        _style.fontStyle = FontStyle.Bold;
    }

    void FixedUpdate()
    {
        SetCarState();
        UpdateCarVariables();
        //TODO:  limit _rb.velocity.magnitude to < maxSpeedBoost
    }

    private void UpdateCarVariables()
    {
        forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
        forwardSpeed = (float)System.Math.Round(forwardSpeed, 2);
        forwardSpeedAbs = Mathf.Abs(forwardSpeed);
        forwardSpeedSign = Mathf.Sign(forwardSpeed);
    }

    void SetCarState()
    {
        int temp = _sphereColliders.Count(c => c.isTouchingSurface);
        numWheelsSurface = temp;

        isAllWheelsSurface = numWheelsSurface >= 3;

        if (isAllWheelsSurface)
            carState = CarStates.AllWheelsSurface;

        if (!isAllWheelsSurface && !isBodySurface)
            carState = CarStates.SomeWheelsSurface;

        if (isBodySurface && !isAllWheelsSurface)
            carState = CarStates.BodySideGround;

        if (isAllWheelsSurface && Vector3.Dot(Vector3.up, transform.up) > 0.95f)
            carState = CarStates.AllWheelsGround;

        if (isBodySurface && Vector3.Dot(Vector3.up, transform.up) < -0.95f)
            carState = CarStates.BodyGroundDead;

        if (!isBodySurface && numWheelsSurface == 0)
            carState = CarStates.Air;

        isCanDrive = carState == CarStates.AllWheelsSurface || carState == CarStates.AllWheelsGround;
    }

    /// <summary>
    /// Capture full car state for server reconciliation.
    /// Called after each physics tick on both server (authoritative) and client (predicted).
    /// </summary>
    public StatePayload CaptureStatePayload(int tick)
    {
        var jumping = GetComponent<CubeJumping>();
        var groundControl = GetComponent<CubeGroundControl>();

        return new StatePayload
        {
            Tick = tick,
            Position = _rb.position,
            Rotation = _rb.rotation,
            Velocity = _rb.linearVelocity,
            AngularVelocity = _rb.angularVelocity,
            IsCanDrive = isCanDrive,
            IsAllWheelsSurface = isAllWheelsSurface,
            NumWheelsSurface = numWheelsSurface,
            IsBodySurface = isBodySurface,
            ForwardSpeed = forwardSpeed,
            ForwardSpeedSign = forwardSpeedSign,
            ForwardSpeedAbs = forwardSpeedAbs,
            CarState = (int)carState,
            IsJumping = jumping != null ? jumping.isJumping : false,
            IsCanFirstJump = jumping != null ? jumping.isCanFirstJump : false,
            IsCanKeepJumping = jumping != null ? jumping.isCanKeepJumping : false,
            JumpTimer = jumping != null ? jumping.jumpTimer : 0f,
            CurrentWheelSideFriction = groundControl != null ? groundControl.currentWheelSideFriction : 8f
        };
    }

    /// <summary>
    /// Restore full car state from a server snapshot for reconciliation rewind.
    /// </summary>
    public void RestoreStatePayload(StatePayload state)
    {
        _rb.position = state.Position;
        _rb.rotation = state.Rotation;
        _rb.linearVelocity = state.Velocity;
        _rb.angularVelocity = state.AngularVelocity;

        isCanDrive = state.IsCanDrive;
        isAllWheelsSurface = state.IsAllWheelsSurface;
        numWheelsSurface = state.NumWheelsSurface;
        isBodySurface = state.IsBodySurface;
        forwardSpeed = state.ForwardSpeed;
        forwardSpeedSign = state.ForwardSpeedSign;
        forwardSpeedAbs = state.ForwardSpeedAbs;
        carState = (CarStates)state.CarState;

        var jumping = GetComponent<CubeJumping>();
        if (jumping != null)
        {
            jumping.isJumping = state.IsJumping;
            jumping.isCanFirstJump = state.IsCanFirstJump;
            jumping.isCanKeepJumping = state.IsCanKeepJumping;
            jumping.jumpTimer = state.JumpTimer;
        }

        var groundControl = GetComponent<CubeGroundControl>();
        if (groundControl != null)
        {
            groundControl.currentWheelSideFriction = state.CurrentWheelSideFriction;
        }
    }

    void DownForce()
    {
        if (carState == CarStates.AllWheelsSurface || carState == CarStates.AllWheelsGround)
            _rb.AddForce(-transform.up * 5, ForceMode.Acceleration);
    }

    #region GUI

    void OnGUI()
    {
        // Only show debug HUD on clients (server has no display)
        if (!IsOwner || _style == null) return;

        GUI.Label(new Rect(10.0f, 40.0f, 150, 130), $"{forwardSpeed:F2} m/s {forwardSpeed * 100:F0} uu/s", _style);
    }

    private void OnDrawGizmos()
    {
        if (_rb == null) return;
        Gizmos.color = Color.black;
        Gizmos.DrawSphere(_rb.transform.TransformPoint(_rb.centerOfMass), 0.03f);
    }

    #endregion
}
