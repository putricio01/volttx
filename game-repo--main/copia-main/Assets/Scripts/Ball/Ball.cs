using System;
using UnityEngine;
using Random = UnityEngine.Random;
using Unity.Netcode;

/// <summary>
/// Ball physics — server authoritative.
/// Server: runs full physics, handles collisions.
/// Client: Rigidbody is kinematic, position comes from BallNetworkController interpolation.
///
/// Car-ball collision is handled via OverlapSphere (not OnCollisionEnter) because
/// Physics.IgnoreCollision is active between cars and ball on ALL instances.
/// This prevents Unity's automatic collision response from tilting the car.
/// </summary>
public class Ball : NetworkBehaviour
{
    [SerializeField] [Range(10, 80)] float randomSpeed = 40;
    [SerializeField] float initialForce = 400;
    [SerializeField] float hitMultiplier = 50;
    [SerializeField] float hitDetectionRadius = 1.5f;
    [SerializeField] float hitCooldown = 0.15f;

    private bool isTouchedGround = false;

    Rigidbody _rb;
    Transform _transform;
    float _lastHitTime;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _transform = this.transform;
        isTouchedGround = false;
    }

#if UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
    void Update()
    {
        // Dev-only input for testing
        if (Input.GetKeyDown(KeyCode.T))
            ShootInRandomDirection(randomSpeed);

        if (Input.GetKeyDown(KeyCode.R))
            ResetBall();

        if (Input.GetButtonDown("Select"))
            ResetShot(new Vector3(7.76f, 2.98f, 0f));
    }
#endif

    private void ResetShot(Vector3 pos)
    {
        if (!IsServer) return;
        _transform.position = pos;
        _rb.linearVelocity = new Vector3(30, 10, 0);
        _rb.angularVelocity = Vector3.zero;
    }

    [ContextMenu("ResetBall")]
    public void ResetBall()
    {
        if (!IsServer) return;
        var desired = new Vector3(0, 12.23f, 0f);
        _transform.SetPositionAndRotation(desired, Quaternion.identity);
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    [ContextMenu("ShootInRandomDirection")]
    private void ShootInRandomDirection(float speed)
    {
        if (!IsServer) return;
        float speedRange = Random.Range(speed - 10, speed + 10);
        var randomDirection = Random.insideUnitCircle.normalized;
        var direction = new Vector3(randomDirection.x, Random.Range(-0.5f, 0.5f), randomDirection.y).normalized;
        _rb.linearVelocity = direction * speedRange;
    }

    /// <summary>
    /// Server-only: detect nearby cars via OverlapSphere and apply hit force.
    /// This replaces OnCollisionEnter for car detection because Physics.IgnoreCollision
    /// is now active between ball and all cars (to prevent car tilting).
    /// Ground/wall collisions still use OnCollisionEnter normally.
    /// </summary>
    void FixedUpdate()
    {
        if (!IsServer) return;

        // Cooldown to prevent multiple hits per contact
        if (Time.time - _lastHitTime < hitCooldown) return;

        Collider[] hits = Physics.OverlapSphere(_transform.position, hitDetectionRadius);
        foreach (Collider hit in hits)
        {
            Rigidbody hitRb = hit.attachedRigidbody;
            if (hitRb == null) continue;

            // Check root tag — car children (wheels, body) share the root Player tag
            if (!hitRb.CompareTag("Player")) continue;

            float carSpeed = hitRb.linearVelocity.magnitude;
            float force = initialForce + carSpeed * hitMultiplier;

            // Blend car's velocity direction (where the car is going) with
            // push-away direction (ball away from car center) for natural feel.
            // At high speed: mostly velocity-based (like a real hit)
            // At low speed: mostly push-away (like a nudge)
            Vector3 pushAway = (_transform.position - hitRb.position).normalized;
            Vector3 carDir = carSpeed > 1f ? hitRb.linearVelocity.normalized : pushAway;
            float velocityBlend = Mathf.Clamp01(carSpeed / 20f);
            Vector3 hitDir = Vector3.Lerp(pushAway, carDir, velocityBlend).normalized;

            _rb.AddForce(hitDir * force);

            // Notify the car's owning client about the impact
            var carNet = hitRb.GetComponent<CarNetworkController>();
            if (carNet != null)
                carNet.ServerNotifyBallImpact();

            _lastHitTime = Time.time;
            break; // one hit per tick max
        }
    }

    /// <summary>
    /// Server-only: ground/wall collisions still use OnCollisionEnter.
    /// Car collisions are handled in FixedUpdate via OverlapSphere.
    /// </summary>
    private void OnCollisionEnter(Collision col)
    {
        if (!IsServer) return;

        if (col.gameObject.CompareTag("Ground"))
        {
            isTouchedGround = true;
        }
    }

    /// <summary>
    /// Server-only ball respawn after a goal.
    /// </summary>
    public bool Respawn()
    {
        if (!IsServer) return true;
        transform.position = new Vector3(0, 1, 0);
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        return true;
    }
}
