using UnityEngine;
using Cinemachine;
public class UICanvasControllerInput : MonoBehaviour
{
    public InputManager inputManager;
     public CinemachineFreeLook cinemachineFreeLookCamera;

    void Start()
    {
       
    }

    

    public void VirtualMoveInput(Vector2 virtualMoveDirection)
    {
        if (inputManager != null)
        {
            inputManager.useJoystickInput = true;  // Switch to joystick input
            inputManager.joystickMoveInput = virtualMoveDirection;  // Update joystick input
            Debug.Log("Joystick Input Received: " + virtualMoveDirection);


            if (virtualMoveDirection != Vector2.zero)
            {
                // Disable mouse input when joystick is active
                cinemachineFreeLookCamera.m_XAxis.m_InputAxisName = ""; // Disable mouse X input
                cinemachineFreeLookCamera.m_YAxis.m_InputAxisName = ""; // Disable mouse Y input
            }
            else
            {
                // Enable mouse input again when joystick input is not active
                cinemachineFreeLookCamera.m_XAxis.m_InputAxisName = "Mouse X"; // Enable mouse X input
                cinemachineFreeLookCamera.m_YAxis.m_InputAxisName = "Mouse Y"; // Enable mouse Y input
            }
        }
    }

    // Other Virtual input handling methods...}


    public void VirtualLookInput(Vector2 virtualLookDirection)
    {
        // Implementation of look direction
    }

    public void VirtualJumpInput(bool virtualJumpState)
    {
        // Implementation of jump input
        if (virtualJumpState == true)
            {
                // Disable mouse input when joystick is active
                cinemachineFreeLookCamera.m_XAxis.m_InputAxisName = ""; // Disable mouse X input
                cinemachineFreeLookCamera.m_YAxis.m_InputAxisName = ""; // Disable mouse Y input
            }
            else
            {
                // Enable mouse input again when joystick input is not active
                cinemachineFreeLookCamera.m_XAxis.m_InputAxisName = "Mouse X"; // Enable mouse X input
                cinemachineFreeLookCamera.m_YAxis.m_InputAxisName = "Mouse Y"; // Enable mouse Y input
            }
    }

    public void VirtualSprintInput(bool virtualSprintState)
    {
        // Implementation of sprint input
        if (virtualSprintState == true)
            {
                // Disable mouse input when joystick is active
                cinemachineFreeLookCamera.m_XAxis.m_InputAxisName = ""; // Disable mouse X input
                cinemachineFreeLookCamera.m_YAxis.m_InputAxisName = ""; // Disable mouse Y input
            }
            else
            {
                // Enable mouse input again when joystick input is not active
                cinemachineFreeLookCamera.m_XAxis.m_InputAxisName = "Mouse X"; // Enable mouse X input
                cinemachineFreeLookCamera.m_YAxis.m_InputAxisName = "Mouse Y"; // Enable mouse Y input
            }
    }

    public void VirtualSwitchInput(bool virtualSwitchState)
    {
        // Implementation of switch mode input
    }
}
