using UnityEditor;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Diagnostic: finds all InputManager components in scene and reports their state.
/// Run this WHILE in play mode after starting host.
/// </summary>
public class DiagnoseInput
{
    public static void Execute()
    {
        // Check active input handling
        var so = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]
        );
        var prop = so.FindProperty("activeInputHandler");
        Debug.Log($"[Diag] activeInputHandler = {prop?.intValue} (0=Both, 1=Old, 2=New)");

        // Check if in play mode
        Debug.Log($"[Diag] Application.isPlaying = {Application.isPlaying}");

        // Check legacy input
        try
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            bool m0 = Input.GetMouseButton(0);
            bool m1 = Input.GetMouseButton(1);
            Debug.Log($"[Diag] Legacy Input test: H={h}, V={v}, Mouse0={m0}, Mouse1={m1}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Diag] Legacy Input FAILED: {e.Message}");
        }

        // Find all InputManagers
        var inputManagers = Object.FindObjectsByType<InputManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[Diag] Found {inputManagers.Length} InputManager(s) in scene");

        foreach (var im in inputManagers)
        {
            var go = im.gameObject;
            var nb = go.GetComponent<NetworkObject>();
            var cnc = go.GetComponent<CarNetworkController>();

            string ownerInfo = "N/A";
            if (nb != null && nb.IsSpawned)
                ownerInfo = $"IsOwner={nb.IsOwner}, IsLocalPlayer={nb.IsLocalPlayer}, OwnerClientId={nb.OwnerClientId}";
            else if (nb != null)
                ownerInfo = "NetworkObject NOT spawned";
            else
                ownerInfo = "No NetworkObject";

            Debug.Log($"[Diag] InputManager on '{go.name}': enabled={im.enabled}, useJoystickInput={im.useJoystickInput}, " +
                      $"throttle={im.throttleInput}, steer={im.steerInput}, boost={im.isBoost}, jump={im.isJump}");
            Debug.Log($"[Diag]   Network: {ownerInfo}");
            Debug.Log($"[Diag]   GameObject active={go.activeSelf}, activeInHierarchy={go.activeInHierarchy}");

            // Check if physics scripts are enabled
            var gc = go.GetComponent<CubeGroundControl>();
            var boost = go.GetComponent<CubeBoosting>();
            var jump = go.GetComponent<CubeJumping>();
            var air = go.GetComponent<CubeAirControl>();
            Debug.Log($"[Diag]   GroundControl={gc?.enabled}, Boosting={boost?.enabled}, Jumping={jump?.enabled}, AirControl={air?.enabled}");
        }

        // Check NetworkManager
        if (NetworkManager.Singleton != null)
        {
            Debug.Log($"[Diag] NetworkManager: IsServer={NetworkManager.Singleton.IsServer}, IsClient={NetworkManager.Singleton.IsClient}, IsHost={NetworkManager.Singleton.IsHost}");
            Debug.Log($"[Diag] Connected clients: {NetworkManager.Singleton.ConnectedClients.Count}");
        }
        else
        {
            Debug.Log("[Diag] NetworkManager.Singleton is null");
        }
    }
}
