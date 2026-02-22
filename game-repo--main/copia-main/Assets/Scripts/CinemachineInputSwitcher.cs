using UnityEngine;
using Cinemachine;

/// <summary>
/// Client-only: Switches Cinemachine FreeLook between joystick and mouse/touch input.
/// Safe on server (null-checks prevent errors when cameras/UI are disabled).
/// </summary>
public class CinemachineInputSwitcher : MonoBehaviour
{
    public CinemachineFreeLook freeLookCamera;
    public Joystick joystick;
    private bool usingJoystick = false;

    private void Update()
    {
        if (joystick == null || freeLookCamera == null) return;
        if (joystick.Horizontal != 0 || joystick.Vertical != 0)
        {
            usingJoystick = true;
            UpdateCinemachineAxisInput();
        }
        else if (usingJoystick)
        {
            usingJoystick = false;
            UpdateCinemachineAxisInput();
        }
    }

    private void UpdateCinemachineAxisInput()
    {
        if (usingJoystick)
        {
            freeLookCamera.m_XAxis.m_InputAxisName = "";
            freeLookCamera.m_YAxis.m_InputAxisName = "";
        }
        else
        {
            freeLookCamera.m_XAxis.m_InputAxisName = "Mouse X";
            freeLookCamera.m_YAxis.m_InputAxisName = "Mouse Y";
        }
    }

    private void LateUpdate()
    {
        if (joystick == null || freeLookCamera == null) return;
        if (usingJoystick)
        {
            freeLookCamera.m_XAxis.Value += joystick.Horizontal * Time.deltaTime * 100f;
            freeLookCamera.m_YAxis.Value += joystick.Vertical * Time.deltaTime * 100f;
        }
    }
}
