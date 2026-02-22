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
    [SerializeField] int stateSendInterval = 6; // Send at 10Hz if physics is 60Hz

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

        // Interpolate between buffered states for smooth rendering
        if (_hasInterpTarget)
        {
            _interpTime += Time.deltaTime;
            float t = Mathf.Clamp01(_interpTime / _interpDuration);

            transform.position = Vector3.Lerp(_interpFrom.Position, _interpTo.Position, t);
            transform.rotation = Quaternion.Slerp(_interpFrom.Rotation, _interpTo.Rotation, t);
        }
    }

    [ClientRpc]
    void BroadcastBallStateClientRpc(BallStatePayload state)
    {
        if (IsServer) return; // Server doesn't need to interpolate

        // Shift interpolation targets
        _interpFrom = _interpTo;
        _interpTo = state;
        _interpTime = 0f;
        _hasInterpTarget = true;

        // Estimate interpolation duration from tick delta
        if (_interpFrom.Tick > 0)
        {
            int tickDelta = _interpTo.Tick - _interpFrom.Tick;
            _interpDuration = Mathf.Max(tickDelta * Time.fixedDeltaTime, 0.016f);
        }
    }
}
