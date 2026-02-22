using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(CubeController))]
public class CubeBoosting : NetworkBehaviour
{
    public float BoostForceMultiplier = 1f;
    const float BoostForce = 991 / 100;

    CubeController _c;
    Rigidbody _rb;
    InputManager _inputManager;

    public override void OnNetworkSpawn()
    {
        // Server doesn't need particle effects
        if (IsServer && !IsHost) return;

        if (!IsOwner)
        {
            var ps = Resources.FindObjectsOfTypeAll<CubeParticleSystem>();
            if (ps.Length > 0) ps[0].gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        _c = GetComponent<CubeController>();
        _rb = GetComponentInParent<Rigidbody>();
        _inputManager = GetComponent<InputManager>();

        // Server doesn't need particle effects
        if (IsServer && !IsHost) return;

        // Activate ParticleSystems GameObject
        var particles = Resources.FindObjectsOfTypeAll<CubeParticleSystem>();
        if (particles.Length > 0 && particles[0] != null && IsOwner)
            particles[0].gameObject.SetActive(true);
    }

    void FixedUpdate()
    {
        Boosting();
    }

    void Boosting()
    {
        if (_inputManager == null) return;
        if (_inputManager.isBoost && _c.forwardSpeed < CubeController.MaxSpeedBoost)
        {
            _rb.AddForce(BoostForce * BoostForceMultiplier * transform.forward, ForceMode.Acceleration);
        }
    }
}
