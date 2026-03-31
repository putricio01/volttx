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

    // Buffered joystick value sampled at FixedUpdate time for consistent tick capture
    Vector2 _fixedJoystickInput;
    bool _loggedJoystickRangeWarning;

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

    void FixedUpdate()
    {
        if (serverMode) return;

        // Snapshot joystick at physics tick time so CaptureInputPayload is consistent
        if (useJoystickInput)
        {
            _fixedJoystickInput = SanitizeJoystickInput(joystickMoveInput);

            // With WASD snap mode, values are already -1, 0, or +1.
            // No quantization needed — digital values match exactly on client and server.
            throttleInput = _fixedJoystickInput.y;
            steerInput = _fixedJoystickInput.x;
            yawInput = steerInput;
            isJumpUp = _latchedJumpUp;
            isJumpDown = _latchedJumpDown;
        }
    }

    void Update()
    {
        // Server only gets input via ApplyInputPayload(), never from local keyboard
        if (serverMode) return;

#if !UNITY_SERVER
        if (useJoystickInput)
        {
            // Input is now captured in FixedUpdate for tick-sync.
            // Only update latched jump events here (edge detection).
            isJumpUp = _latchedJumpUp;
            isJumpDown = _latchedJumpDown;
        }
        else
        {
#if ENABLE_LEGACY_INPUT_MANAGER
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
#else
            throttleInput = 0f;
            steerInput = 0f;
            yawInput = 0f;
            pitchInput = 0f;
            rollInput = 0f;
            isJump = false;
            isJumpUp = false;
            isJumpDown = false;
            isBoost = false;
            isDrift = false;
            isAirRoll = false;
            _latchedJumpDown = false;
            _latchedJumpUp = false;
#endif
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
        payload.ClampAnalogInputs();

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
        payload.ClampAnalogInputs();
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
        // Mirror keyboard behavior: press/release are one-shot edges consumed in FixedUpdate.
        if (state)
        {
            isJump = true;
            _latchedJumpDown = true;
        }
        else
        {
            isJump = false;
            _latchedJumpUp = true;
        }

        isJumpUp = _latchedJumpUp;
        isJumpDown = _latchedJumpDown;
    }

    private static float GetRollInput()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        var inputRoll = 0;
        if (Input.GetKey(KeyCode.E) || Input.GetButton("B"))
            inputRoll = -1;
        else if (Input.GetKey(KeyCode.Q) || Input.GetButton("Y"))
            inputRoll = 1;
        return inputRoll;
#else
        return 0f;
#endif
    }

    static float GetThrottle()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        float throttle = 0;
        if (Input.GetAxis("Vertical") > 0 || Input.GetAxis("RT") > 0)
            throttle = Mathf.Max(Input.GetAxis("Vertical"), Input.GetAxis("RT"));
        else if (Input.GetAxis("Vertical") < 0 || Input.GetAxis("LT") < 0)
            throttle = Mathf.Min(Input.GetAxis("Vertical"), Input.GetAxis("LT"));
        return throttle;
#else
        return 0f;
#endif
    }

    static float GetSteerInput()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetAxis("Horizontal");
#else
        return 0f;
#endif
    }

    public string axisName = "Horizontal";
    public AnimationCurve sensitivityCurve;
    private float _vel = 0;
    float _currentHorizontalInput = 0;
    public float GetValue()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        var target = Mathf.MoveTowards(_currentHorizontalInput, Input.GetAxis(axisName), Time.fixedDeltaTime / 25);
        _currentHorizontalInput = sensitivityCurve.Evaluate(Mathf.Abs(target));
        var ret = _currentHorizontalInput * Mathf.Sign(Input.GetAxis(axisName));
        return ret;
#else
        return 0f;
#endif
    }

    /// <summary>
    /// Quantize a float to the nearest step.
    /// Reduces unique analog values so client/server predictions match more often.
    /// </summary>
    static float Quantize(float value, float step)
    {
        return Mathf.Round(value / step) * step;
    }

    Vector2 SanitizeJoystickInput(Vector2 rawInput)
    {
        if (!float.IsFinite(rawInput.x) || !float.IsFinite(rawInput.y))
            return Vector2.zero;

        Vector2 clampedInput = Vector2.ClampMagnitude(rawInput, 1f);

        if (!_loggedJoystickRangeWarning && (Mathf.Abs(rawInput.x) > 1.01f || Mathf.Abs(rawInput.y) > 1.01f || rawInput.sqrMagnitude > 1.05f))
        {
            _loggedJoystickRangeWarning = true;
            Debug.LogWarning($"[InputManager] Joystick input out of range on '{name}'. Raw={rawInput} Clamped={clampedInput}. This usually means the mobile move control is scaled for UI but not normalized for gameplay.");
        }

        return clampedInput;
    }
}
