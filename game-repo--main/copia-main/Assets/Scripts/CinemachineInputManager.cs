using UnityEngine;
using Cinemachine;
using UnityEngine.EventSystems;

public class CinemachineInputManager : MonoBehaviour
{
    public CinemachineFreeLook freeLookCamera; // Reference to your Cinemachine FreeLook camera

    private bool isUIInteracting;

    void Update()
    {
        // Check if the pointer is over a UI element
        if (EventSystem.current.IsPointerOverGameObject())
        {
            if (!isUIInteracting)
            {
                DisableCinemachineInput();
                isUIInteracting = true;
            }
        }
        else
        {
            if (isUIInteracting)
            {
                EnableCinemachineInput();
                isUIInteracting = false;
            }
        }
    }

    private void DisableCinemachineInput()
    {
        freeLookCamera.m_XAxis.m_InputAxisName = string.Empty; // Disable X-axis mouse input
        freeLookCamera.m_YAxis.m_InputAxisName = string.Empty; // Disable Y-axis mouse input
    }

    private void EnableCinemachineInput()
    {
        freeLookCamera.m_XAxis.m_InputAxisName = "Mouse X"; // Restore X-axis input
        freeLookCamera.m_YAxis.m_InputAxisName = "Mouse Y"; // Restore Y-axis input
    }
}
