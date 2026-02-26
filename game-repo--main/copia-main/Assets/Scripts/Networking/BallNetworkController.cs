using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-authoritative ball state broadcast + client-side interpolation.
/// Server: captures ball state each tick and broadcasts to all clients.
/// Client: sets ball Rigidbody to kinematic and interpolates between received states.
/// Uses NetworkTickSystem for synchronized ticks.
/// Uses runtime checks instead of #if UNITY_SERVER for MPPM compatibility.
/// </summary>
[DefaultExecutionOrder(100)]
public class BallNetworkController : NetworkBehaviour
{
    [SerializeField] int stateSendInterval = 1; // Send every tick = 60Hz

    Rigidbody _rb;
    int _ticksSinceLastSend = 0;
    int _currentTick = 0;
    int _lastProcessedTick = -1;

    // Client interpolation
    BallStatePayload _interpFrom;
    BallStatePayload _interpTo;
    float _interpTime = 0f;
    float _interpDuration = 0.066f; // ~2 ticks at 30Hz
    bool _hasInterpTarget = false;

    // Extrapolation
    float _maxExtrapolationTime = 0.1f; // cap at 100ms
    bool _isExtrapolating = false;

    // World-space gravity for extrapolation (BallPhysics uses -650 in internal units / 100 scale)
    static readonly Vector3 BallGravity = new Vector3(0f, -6.5f, 0f);

    int GetSyncedTick()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkTickSystem == null)
            return _currentTick;
        return NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick;
    }

    public override void OnNetworkSpawn()
    {
        _rb = GetComponent<Rigidbody>();

        if (IsServer)
        {
            // Server: ball physics runs normally (non-kinematic)
            Debug.Log("[Server] Ball spawned — server authoritative physics");
        }

        if (IsClient && !IsServer)
        {
            // Client-only: ball is kinematic, we interpolate from server state
            _rb.isKinematic = true;
            Debug.Log("[Client] Ball spawned — interpolation mode");
        }
    }

    void FixedUpdate()
    {
        _currentTick = GetSyncedTick();

        // Guard against duplicate ticks
        if (_currentTick == _lastProcessedTick)
            return;
        _lastProcessedTick = _currentTick;

        if (IsServer)
        {
            _ticksSinceLastSend++;
            if (_ticksSinceLastSend >= stateSendInterval)
            {
                _ticksSinceLastSend = 0;

                BallStatePayload state = new BallStatePayload
                {
                    Tick = _currentTick,
                    Position = _rb.position,
                    Rotation = _rb.rotation,
                    Velocity = _rb.linearVelocity,
                    AngularVelocity = _rb.angularVelocity
                };

                BroadcastBallStateClientRpc(state);
            }
        }
    }

    void LateUpdate()
    {
        if (IsServer) return;

        if (!_hasInterpTarget) return;

        _interpTime += Time.deltaTime;
        float t = _interpTime / _interpDuration;

        if (t <= 1.0f)
        {
            // Normal interpolation — cubic Hermite using velocity as tangents
            _isExtrapolating = false;
            t = Mathf.Clamp01(t);

            Vector3 p0 = _interpFrom.Position;
            Vector3 p1 = _interpTo.Position;
            Vector3 m0 = _interpFrom.Velocity * _interpDuration;
            Vector3 m1 = _interpTo.Velocity * _interpDuration;

            transform.position = HermitePosition(t, p0, p1, m0, m1);
            transform.rotation = Quaternion.Slerp(_interpFrom.Rotation, _interpTo.Rotation, t);
        }
        else
        {
            // Extrapolation fallback — no new state arrived yet
            float extraTime = _interpTime - _interpDuration;

            if (extraTime <= _maxExtrapolationTime)
            {
                _isExtrapolating = true;

                // Extrapolate with velocity + gravity for ballistic arc
                transform.position = _interpTo.Position
                    + _interpTo.Velocity * extraTime
                    + 0.5f * BallGravity * extraTime * extraTime;

                // Rotation extrapolation using angular velocity
                Vector3 angVel = _interpTo.AngularVelocity;
                if (angVel.sqrMagnitude > 0.001f)
                {
                    float angSpeed = angVel.magnitude;
                    Quaternion extraRot = Quaternion.AngleAxis(
                        angSpeed * Mathf.Rad2Deg * extraTime,
                        angVel.normalized
                    );
                    transform.rotation = extraRot * _interpTo.Rotation;
                }
            }
            // else: cap reached, hold position
        }
    }

    static Vector3 HermitePosition(float t, Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
    }

    [ClientRpc]
    void BroadcastBallStateClientRpc(BallStatePayload state)
    {
        if (IsServer) return; // Server doesn't need to interpolate

        // Shift interpolation targets
        _interpFrom = _interpTo;
        _interpTo = state;
        _interpTime = 0f;
        _isExtrapolating = false;
        _hasInterpTarget = true;

        // Estimate interpolation duration from tick delta
        if (_interpFrom.Tick > 0)
        {
            int tickDelta = _interpTo.Tick - _interpFrom.Tick;
            _interpDuration = Mathf.Max(tickDelta * Time.fixedDeltaTime, 0.016f);
        }
    }
}
