using UnityEngine;
using Unity.Netcode;
using Cinemachine;

/// <summary>
/// Network spawn handler. Sets up Cinemachine camera priority based on ownership.
/// Spawn point assignment is now handled by PlayerRespawner (server-side).
/// </summary>
public class netwex : NetworkBehaviour
{
    [SerializeField] public CinemachineFreeLook vcc;
    public Transform[] spawnPoints;

    public override void OnNetworkSpawn()
    {
        // Server doesn't need cameras at all
        if (IsServer && !IsHost)
        {
            if (vcc != null) vcc.gameObject.SetActive(false);
            return;
        }

        if (vcc == null) return;

        if (IsOwner)
        {
            vcc.Priority = 1;
        }
        else
        {
            vcc.Priority = 0;
        }

        // Note: Spawn point assignment is now handled server-side by PlayerRespawner.
        // This script only manages client-side camera priority.
    }
}
