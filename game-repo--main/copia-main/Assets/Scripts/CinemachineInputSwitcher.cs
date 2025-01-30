using UnityEngine;
using Cinemachine;

public class CinemachineInputSwitcher : MonoBehaviour
{
    public CinemachineFreeLook freeLookCamera; // Drag your CinemachineFreeLook camera here
    public Joystick joystick;                  // Reference to your joystick
    private bool usingJoystick = false;        // Tracks when joystick is in use

    private void Update()
    {
        // Check joystick input
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
            // Clear input axis names to override with joystick input
            freeLookCamera.m_XAxis.m_InputAxisName = "";
            freeLookCamera.m_YAxis.m_InputAxisName = "";
        }
        else
        {
            // Set axis names back to default for touch input
            freeLookCamera.m_XAxis.m_InputAxisName = "Mouse X";
            freeLookCamera.m_YAxis.m_InputAxisName = "Mouse Y";
        }
    }

    private void LateUpdate()
    {
        if (usingJoystick)
        {
            // Apply joystick input directly to Cinemachine axis values
            freeLookCamera.m_XAxis.Value += joystick.Horizontal * Time.deltaTime * 100f;
            freeLookCamera.m_YAxis.Value += joystick.Vertical * Time.deltaTime * 100f;
        }
    }
}
