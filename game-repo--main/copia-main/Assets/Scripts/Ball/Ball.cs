using System;
using UnityEngine;
using Random = UnityEngine.Random;
using Unity.Netcode;

/// <summary>
/// Ball physics — server authoritative.
/// Server: runs full physics, handles collisions.
/// Client: Rigidbody is kinematic, position comes from BallNetworkController interpolation.
/// </summary>
public class Ball : NetworkBehaviour
{
    [SerializeField] [Range(10, 80)] float randomSpeed = 40;
    [SerializeField] float initialForce = 400;
    [SerializeField] float hitMultiplier = 50;

    private bool isTouchedGround = false;

    Rigidbody _rb;
    Transform _transform;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _transform = this.transform;
        isTouchedGround = false;
    }

#if UNITY_EDITOR
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
    /// Server-only collision handling. No more ownership transfer —
    /// the server owns the ball at all times.
    /// Uses a blend of car velocity direction and push-away direction
    /// for more natural-feeling ball physics.
    /// </summary>
    private void OnCollisionEnter(Collision col)
    {
        // Only the server processes ball collisions
        if (!IsServer) return;

        Rigidbody colRb = col.rigidbody;
        bool hitPlayer = false;
        if (colRb != null)
        {
            // Use Rigidbody/root tag so hits register from any child collider
            hitPlayer = colRb.CompareTag("Player");
        }
        else
        {
            hitPlayer = col.transform.root.CompareTag("Player");
        }

        if (hitPlayer)
        {
            float carSpeed = colRb != null ? colRb.linearVelocity.magnitude : 0f;
            float force = initialForce + carSpeed * hitMultiplier;

            // Blend car's velocity direction (where the car is going) with
            // push-away direction (ball away from car center) for natural feel.
            // At high speed: mostly velocity-based (like a real hit)
            // At low speed: mostly push-away (like a nudge)
            Vector3 pushAway = (_transform.position - col.transform.position).normalized;
            Vector3 carDir = (colRb != null && carSpeed > 1f) ? colRb.linearVelocity.normalized : pushAway;
            float velocityBlend = Mathf.Clamp01(carSpeed / 20f); // full blend at 20+ speed
            Vector3 hitDir = Vector3.Lerp(pushAway, carDir, velocityBlend).normalized;

            _rb.AddForce(hitDir * force);
        }

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
