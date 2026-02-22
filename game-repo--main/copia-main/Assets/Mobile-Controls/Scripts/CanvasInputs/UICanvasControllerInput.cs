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
            inputManager.joystickMoveInput = virtualMoveDirection;
        }
    }

    // Other Virtual input handling methods...}


    public void VirtualLookInput(Vector2 virtualLookDirection)
    {
        // Implementation of look direction
    }

    public void VirtualJumpInput(bool virtualJumpState)
    {
        if (inputManager != null)
            inputManager.OnJumpButtonClicked(virtualJumpState);
    }

    public void VirtualSprintInput(bool virtualSprintState)
    {
        if (inputManager != null)
            inputManager.SetBoost(virtualSprintState);
    }

    public void VirtualSwitchInput(bool virtualSwitchState)
    {
        // Implementation of switch mode input
    }
}
