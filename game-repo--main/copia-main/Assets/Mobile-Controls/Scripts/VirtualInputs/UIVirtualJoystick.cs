using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

public class UIVirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [System.Serializable]
    public class Event : UnityEvent<Vector2> { }

    [Header("Rect References")]
    public RectTransform containerRect;
    public RectTransform handleRect;

    [Header("Settings")]
    public float joystickRange = 50f;
    public float magnitudeMultiplier = 1f;
    public bool invertXOutputValue;
    public bool invertYOutputValue;

    [Header("WASD Snap Mode")]
    [Tooltip("Snap output to -1, 0, or +1 per axis (like WASD keys)")]
    public bool wasdSnapMode = false;
    [Tooltip("Normalized threshold (0-1) before axis snaps to 1/-1")]
    public float snapThreshold = 0.3f;

    [Header("Analog Mode (when WASD snap is off)")]
    [Tooltip("Ignore input below this magnitude (0-1)")]
    public float deadzone = 0.06f;
    [Tooltip("When enabled, analog output eases toward the target instead of matching the finger immediately")]
    public bool smoothAnalogOutput;
    [Tooltip("Smooth input over time for less jitter")]
    public float smoothSpeed = 15f;

    [Header("Output")]
    public Event joystickOutputEvent;

    Vector2 _currentOutput;
    Vector2 _targetOutput;
    bool _isTouching;

    void Start()
    {
        SetupHandle();
    }

    void Update()
    {
        if (wasdSnapMode)
        {
            // Immediate: no smoothing, no lerp. Output matches target exactly.
            _currentOutput = _isTouching ? _targetOutput : Vector2.zero;
        }
        else
        {
            Vector2 target = _isTouching ? _targetOutput : Vector2.zero;
            if (smoothAnalogOutput && smoothSpeed > 0f)
                _currentOutput = Vector2.Lerp(_currentOutput, target, Time.unscaledDeltaTime * smoothSpeed);
            else
                _currentOutput = target;
        }
        OutputPointerEventValue(_currentOutput);
    }

    private void SetupHandle()
    {
        if (handleRect)
        {
            UpdateHandleRectPosition(Vector2.zero);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isTouching = true;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(containerRect, eventData.position, eventData.pressEventCamera, out Vector2 localPos);

        // Normalize to -1..1 range
        Vector2 normalized = ApplySizeDelta(localPos);
        Vector2 clamped = ClampValuesToMagnitude(normalized);

        Vector2 output;
        if (wasdSnapMode)
        {
            // Snap each axis independently to -1, 0, or +1
            float snappedX = Mathf.Abs(clamped.x) > snapThreshold ? Mathf.Sign(clamped.x) : 0f;
            float snappedY = Mathf.Abs(clamped.y) > snapThreshold ? Mathf.Sign(clamped.y) : 0f;
            output = new Vector2(snappedX, snappedY);
        }
        else
        {
            // Analog mode: deadzone + remap
            if (clamped.magnitude < deadzone)
            {
                output = Vector2.zero;
            }
            else
            {
                float remapped = (clamped.magnitude - deadzone) / (1f - deadzone);
                output = clamped.normalized * remapped;
            }
        }

        output = ApplyInversionFilter(output);
        _targetOutput = output * magnitudeMultiplier;

        // Handle visual always follows finger smoothly (regardless of snap mode)
        if (handleRect)
        {
            Vector2 handlePos = Vector2.ClampMagnitude(localPos, joystickRange);
            UpdateHandleRectPosition(handlePos);
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isTouching = false;
        _targetOutput = Vector2.zero;
        _currentOutput = Vector2.zero;

        if (handleRect)
        {
            UpdateHandleRectPosition(Vector2.zero);
        }
    }

    private void OutputPointerEventValue(Vector2 pointerPosition)
    {
        joystickOutputEvent.Invoke(pointerPosition);
    }

    private void UpdateHandleRectPosition(Vector2 newPosition)
    {
        handleRect.anchoredPosition = newPosition;
    }

    Vector2 ApplySizeDelta(Vector2 position)
    {
        float x = containerRect.sizeDelta.x > 0 ? position.x / (containerRect.sizeDelta.x * 0.5f) : 0f;
        float y = containerRect.sizeDelta.y > 0 ? position.y / (containerRect.sizeDelta.y * 0.5f) : 0f;
        return new Vector2(x, y);
    }

    Vector2 ClampValuesToMagnitude(Vector2 position)
    {
        return Vector2.ClampMagnitude(position, 1);
    }

    Vector2 ApplyInversionFilter(Vector2 position)
    {
        if (invertXOutputValue) position.x = -position.x;
        if (invertYOutputValue) position.y = -position.y;
        return position;
    }
}
