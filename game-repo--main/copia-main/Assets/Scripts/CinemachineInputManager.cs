using UnityEngine;
using Cinemachine;
using UnityEngine.EventSystems;

/// <summary>
/// Client-only: Disables Cinemachine input when interacting with UI.
/// Safe on server (null-checks prevent errors when cameras/UI are disabled).
/// </summary>
public class CinemachineInputManager : MonoBehaviour
{
    public CinemachineFreeLook freeLookCamera;

    private bool isUIInteracting;

    void Update()
    {
        if (freeLookCamera == null || EventSystem.current == null) return;

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
        freeLookCamera.m_XAxis.m_InputAxisName = string.Empty;
        freeLookCamera.m_YAxis.m_InputAxisName = string.Empty;
    }

    private void EnableCinemachineInput()
    {
        freeLookCamera.m_XAxis.m_InputAxisName = "Mouse X";
        freeLookCamera.m_YAxis.m_InputAxisName = "Mouse Y";
    }
}
