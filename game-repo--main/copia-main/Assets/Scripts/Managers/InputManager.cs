using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class InputManager : NetworkBehaviour
{
    public float throttleInput, steerInput, yawInput, pitchInput, rollInput;
    public bool isBoost, isDrift, isAirRoll;
    public bool isJump, isJumpUp, isJumpDown;
    
    // Flag to switch between joystick and keyboard input
    public bool useJoystickInput = true; 

    // Joystick values (set from UICanvasControllerInput)
    public Vector2 joystickMoveInput = Vector2.zero;

    public Button bust;
    public Button jomp;

    void Start()
    {
        // Automatically set useJoystickInput based on the platform
        #if UNITY_ANDROID || UNITY_IOS
            useJoystickInput = true;
        #else
            useJoystickInput = false;
        #endif
    }

    void Update()
    {
        if (useJoystickInput)
        {
            // Use joystick values
            Debug.Log("Using Joystick Input");
            throttleInput = joystickMoveInput.y;
            steerInput = joystickMoveInput.x;
        }
        else
        {
            // Use keyboard values (WASD)
            Debug.Log("Using Keyboard Input");
            throttleInput = GetThrottle();
            steerInput = GetSteerInput();
            isJump = Input.GetMouseButton(1) || Input.GetButton("A");
            isJumpUp = Input.GetMouseButtonUp(1) || Input.GetButtonUp("A");
            isJumpDown = Input.GetMouseButtonDown(1) || Input.GetButtonDown("A");
            isBoost = Input.GetButton("RB") || Input.GetMouseButton(0);
        }

        yawInput = Input.GetAxis("Horizontal");
        pitchInput = Input.GetAxis("PitchAxis");
        rollInput = GetRollInput();

        isDrift = Input.GetButton("LB") || Input.GetKey(KeyCode.LeftShift);
        isAirRoll = Input.GetButton("LB") || Input.GetKey(KeyCode.LeftShift);
    }

    public void SetBoost(bool state)
    {
        isBoost = state;
        Debug.Log($"SetBoost called with state: {state}. isBoost is now: {isBoost}");
    }

    public void OnJumpButtonClicked(bool state)
    {
        isJump = state;
        isJumpUp = state;
        isJumpDown = state;
        Debug.Log("Jump button clicked. isJump is now: " + state);
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
