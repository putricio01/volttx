using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Per-player input manager. Each car prefab has its own InputManager.
/// On clients: reads local input from keyboard/joystick in Update().
/// On server: fields are populated from InputPayload via ApplyInputPayload().
/// </summary>
public class InputManager : MonoBehaviour
{
    public float throttleInput, steerInput, yawInput, pitchInput, rollInput;
    public bool isBoost, isDrift, isAirRoll;
    public bool isJump, isJumpUp, isJumpDown;

    // Latched one-shot jump events — survive across Update() frames
    // until consumed by CaptureInputPayload() in FixedUpdate().
    bool _latchedJumpDown;
    bool _latchedJumpUp;

    // Flag to switch between joystick and keyboard input
    public bool useJoystickInput = true;

    /// <summary>
    /// When true, this InputManager only receives input via ApplyInputPayload() (server mode).
    /// When false, it reads local keyboard/joystick input (client owner mode).
    /// Set by CarNetworkController.OnNetworkSpawn().
    /// </summary>
    [HideInInspector] public bool serverMode = false;

    // Joystick values (set from UICanvasControllerInput)
    public Vector2 joystickMoveInput = Vector2.zero;

#if !UNITY_SERVER
    public Button bust;
    public Button jomp;
#endif

    void Start()
    {
#if !UNITY_SERVER
        // Automatically set useJoystickInput based on the platform
        #if UNITY_ANDROID || UNITY_IOS
            useJoystickInput = true;
        #else
            useJoystickInput = false;
        #endif

        // Wire up mobile UI buttons for press/release
        if (bust != null)
        {
            AddPointerEvent(bust.gameObject, EventTriggerType.PointerDown, _ => SetBoost(true));
            AddPointerEvent(bust.gameObject, EventTriggerType.PointerUp, _ => SetBoost(false));
        }
        if (jomp != null)
        {
            AddPointerEvent(jomp.gameObject, EventTriggerType.PointerDown, _ => OnJumpButtonClicked(true));
            AddPointerEvent(jomp.gameObject, EventTriggerType.PointerUp, _ => OnJumpButtonClicked(false));
        }
#endif
    }

#if !UNITY_SERVER
    static void AddPointerEvent(GameObject obj, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
    {
        var trigger = obj.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = obj.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }
#endif

    void Update()
    {
        // Server only gets input via ApplyInputPayload(), never from local keyboard
        if (serverMode) return;

#if !UNITY_SERVER
        if (useJoystickInput)
        {
            throttleInput = joystickMoveInput.y;
            steerInput = joystickMoveInput.x;
            yawInput = joystickMoveInput.x;
            // boost and jump are set via UI button callbacks (SetBoost / OnJumpButtonClicked)
        }
        else
        {
            throttleInput = GetThrottle();
            steerInput = GetSteerInput();
            isJump = Input.GetMouseButton(1) || Input.GetButton("A");
            // Latch one-shot events: once true, stay true until consumed
            if (Input.GetMouseButtonUp(1) || Input.GetButtonUp("A"))
                _latchedJumpUp = true;
            if (Input.GetMouseButtonDown(1) || Input.GetButtonDown("A"))
                _latchedJumpDown = true;
            isJumpUp = _latchedJumpUp;
            isJumpDown = _latchedJumpDown;
            isBoost = Input.GetButton("RB") || Input.GetMouseButton(0);
            yawInput = Input.GetAxis("Horizontal");
            pitchInput = Input.GetAxis("PitchAxis");
            rollInput = GetRollInput();
            isDrift = Input.GetButton("LB") || Input.GetKey(KeyCode.LeftShift);
            isAirRoll = Input.GetButton("LB") || Input.GetKey(KeyCode.LeftShift);
        }
#endif
    }

    /// <summary>
    /// Snapshot current input state into a tick-stamped payload for sending to the server.
    /// Called by the owning client each physics tick.
    /// </summary>
    public InputPayload CaptureInputPayload(int tick)
    {
        var payload = new InputPayload
        {
            Tick = tick,
            ThrottleInput = throttleInput,
            SteerInput = steerInput,
            YawInput = yawInput,
            PitchInput = pitchInput,
            RollInput = rollInput,
            IsBoost = isBoost,
            IsDrift = isDrift,
            IsAirRoll = isAirRoll,
            IsJump = isJump,
            IsJumpUp = isJumpUp,
            IsJumpDown = isJumpDown
        };

        // Clear latched one-shot events after they've been captured
        _latchedJumpDown = false;
        _latchedJumpUp = false;

        return payload;
    }

    /// <summary>
    /// Apply a received InputPayload to populate this InputManager's fields.
    /// Called by the server before running physics for a player.
    /// </summary>
    public void ApplyInputPayload(InputPayload payload)
    {
        throttleInput = payload.ThrottleInput;
        steerInput = payload.SteerInput;
        yawInput = payload.YawInput;
        pitchInput = payload.PitchInput;
        rollInput = payload.RollInput;
        isBoost = payload.IsBoost;
        isDrift = payload.IsDrift;
        isAirRoll = payload.IsAirRoll;
        isJump = payload.IsJump;
        isJumpUp = payload.IsJumpUp;
        isJumpDown = payload.IsJumpDown;
    }

    public void SetBoost(bool state)
    {
        isBoost = state;
    }

    public void OnJumpButtonClicked(bool state)
    {
        isJump = state;
        isJumpUp = state;
        isJumpDown = state;
    }

    private static float GetRollInput()
    {
        var inputRoll = 0;
        if (Input.GetKey(KeyCode.E) || Input.GetButton("B"))
            inputRoll = -1;
        else if (Input.GetKey(KeyCode.Q) || Input.GetButton("Y"))
            inputRoll = 1;
        return inputRoll;
    }

    static float GetThrottle()
    {
        float throttle = 0;
        if (Input.GetAxis("Vertical") > 0 || Input.GetAxis("RT") > 0)
            throttle = Mathf.Max(Input.GetAxis("Vertical"), Input.GetAxis("RT"));
        else if (Input.GetAxis("Vertical") < 0 || Input.GetAxis("LT") < 0)
            throttle = Mathf.Min(Input.GetAxis("Vertical"), Input.GetAxis("LT"));
        return throttle;
    }

    static float GetSteerInput()
    {
        return Input.GetAxis("Horizontal");
    }

    public string axisName = "Horizontal";
    public AnimationCurve sensitivityCurve;
    private float _vel = 0;
    float _currentHorizontalInput = 0;
    public float GetValue()
    {
        var target = Mathf.MoveTowards(_currentHorizontalInput, Input.GetAxis(axisName), Time.fixedDeltaTime / 25);
        _currentHorizontalInput = sensitivityCurve.Evaluate(Mathf.Abs(target));
        var ret = _currentHorizontalInput * Mathf.Sign(Input.GetAxis(axisName));
        return ret;
    }
}
