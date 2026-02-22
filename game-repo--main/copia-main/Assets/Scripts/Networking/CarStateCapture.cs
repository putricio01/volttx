using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Runs AFTER all physics scripts (execution order +100) to capture state.
/// Must be on the same GameObject as CarNetworkController.
/// Uses runtime checks instead of #if UNITY_SERVER for MPPM compatibility.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(CarNetworkController))]
public class CarStateCapture : MonoBehaviour
{
    CarNetworkController _networkController;
    NetworkObject _networkObject;

    void Start()
    {
        _networkController = GetComponent<CarNetworkController>();
        _networkObject = GetComponent<NetworkObject>();
    }

    void FixedUpdate()
    {
        // Skip during reconciliation resimulation (Physics.Simulate handles this)
        if (CarNetworkController.IsResimulating) return;

        // Server captures authoritative state and broadcasts it
        if (_networkObject != null && _networkObject.IsSpawned &&
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            _networkController.OnPostPhysics();
        }

        // Client owner captures predicted state for reconciliation
        if (_networkObject != null && _networkObject.IsSpawned &&
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            _networkController.OnPostPhysicsClient();
        }
    }
}
