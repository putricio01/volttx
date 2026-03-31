using System.Collections.Generic;
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
    /// <summary>
    /// Singleton so owner cars can find the ball and set up Physics.IgnoreCollision.
    /// Only valid on clients; server doesn't need it.
    /// </summary>
    public static BallNetworkController Instance { get; private set; }

    const int DEFAULT_SNAPSHOT_BUFFER = 32;

    struct TimedBallSnapshot
    {
        public BallStatePayload State;
        public float ArrivalTime;
    }

    [SerializeField] int stateSendInterval = 1; // Send every network tick when set to 1
    [Header("Remote Snapshot Interpolation")]
    [SerializeField, Range(20f, 250f)] float interpolationBackTimeMs = 40f;
    [SerializeField, Min(2)] int maxSnapshots = DEFAULT_SNAPSHOT_BUFFER;

    [Header("Forward Temporal Compensation")]
    [Tooltip("Project each incoming snapshot forward by estimated one-way latency " +
             "so the rendered ball is closer to its real-time server position.")]
    [SerializeField] bool forwardCompensation = true;
    [Tooltip("Fraction of the measured snapshot age to compensate. " +
             "1.0 = full compensation (aggressive, may overshoot on jitter spikes). " +
             "0.5 = half compensation (conservative, still cuts perceived lag in half).")]
    [SerializeField, Range(0.1f, 1f)] float compensationFraction = 0.6f;
    [Tooltip("Maximum forward projection in seconds. " +
             "Caps compensation so huge latency spikes don't teleport the ball.")]
    [SerializeField, Range(0.02f, 0.3f)] float maxCompensationTime = 0.15f;

    Rigidbody _rb;
    int _ticksSinceLastSend = 0;
    int _currentTick = 0;
    int _lastProcessedTick = -1;

    // Client interpolation
    readonly List<TimedBallSnapshot> _snapshots = new List<TimedBallSnapshot>(DEFAULT_SNAPSHOT_BUFFER);
    int _latestSnapshotTick = -1;
    bool _hasInterpTarget = false;
    float _adaptiveBackTimeMs;
    float _lastSnapshotArrivalTime;
    float _arrivalJitter;

    // Extrapolation
    float _maxExtrapolationTime = 0.1f; // cap at 100ms
    bool _isExtrapolating = false;
    int _debugExtrapolationCount;

    // Forward temporal compensation state
    float _smoothedSnapshotAgeSec; // EMA of measured snapshot age (seconds)
    int _compensatedSnapshotCount;

    // Periodic diagnostic log
    float _nextDiagLogTime;
    const float DIAG_LOG_INTERVAL = 5f;

    // World-space gravity for extrapolation (BallPhysics uses -650 in internal units / 100 scale)
    static readonly Vector3 BallGravity = new Vector3(0f, -6.5f, 0f);

    public float DebugAdaptiveBackTimeMs => _adaptiveBackTimeMs > 0f ? _adaptiveBackTimeMs : interpolationBackTimeMs;
    public float DebugArrivalJitterMs => _arrivalJitter * 1000f;
    public int DebugSnapshotCount => _snapshots.Count;
    public int DebugExtrapolationCount => _debugExtrapolationCount;
    public float DebugCompensationMs => _smoothedSnapshotAgeSec * compensationFraction * 1000f;
    public int DebugCompensatedSnapshots => _compensatedSnapshotCount;
    public bool DebugIsExtrapolating => _isExtrapolating;

    int GetSyncedTick()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkTickSystem == null)
            return _currentTick;
        return NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick;
    }

    public override void OnNetworkSpawn()
    {
        _rb = GetComponent<Rigidbody>();
        _adaptiveBackTimeMs = interpolationBackTimeMs;
        _lastSnapshotArrivalTime = 0f;
        _arrivalJitter = 0f;
        _debugExtrapolationCount = 0;
        _smoothedSnapshotAgeSec = 0f;
        _compensatedSnapshotCount = 0;

        if (IsServer)
        {
            // Server: ball physics runs normally (non-kinematic).
            // Ignore physical collision with ALL cars so Unity doesn't apply
            // reaction forces that tilt cars. Ball.cs detects hits via overlap.
            var serverCars = FindObjectsByType<CarNetworkController>(FindObjectsSortMode.None);
            foreach (var car in serverCars)
            {
                Collider[] carColliders = car.GetComponentsInChildren<Collider>();
                IgnoreCollisionWithCar(carColliders);
            }
            Debug.Log("[Server] Ball spawned — server authoritative physics (collision ignored with cars)");
        }

        if (IsClient && !IsServer)
        {
            // Client-only: ball is kinematic, we interpolate from server state
            _rb.isKinematic = true;
            _snapshots.Clear();
            _latestSnapshotTick = -1;
            _hasInterpTarget = false;

            Instance = this;

            // Ignore collision with owner car so prediction doesn't collide
            // with the kinematic ball (which would act like an immovable wall).
            var cars = FindObjectsByType<CarNetworkController>(FindObjectsSortMode.None);
            foreach (var car in cars)
            {
                if (car.IsOwner && !car.IsServer)
                {
                    Collider[] carColliders = car.GetComponentsInChildren<Collider>();
                    IgnoreCollisionWithCar(carColliders);
                }
            }

            Debug.Log("[Client] Ball spawned — interpolation mode");
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Disable physics collision between all ball colliders and the given car colliders.
    /// Called on BOTH server and clients:
    /// - Server: prevents Unity's automatic collision response from tilting the car
    ///   (Ball.cs detects hits via overlap instead of OnCollisionEnter).
    /// - Client: prevents the owner car's prediction from colliding with the
    ///   kinematic ball (which would act like an immovable wall).
    /// Does NOT affect triggers or raycasts.
    /// </summary>
    public void IgnoreCollisionWithCar(Collider[] carColliders, bool ignore = true)
    {
        Collider[] ballColliders = GetComponentsInChildren<Collider>();
        foreach (var ballCol in ballColliders)
            foreach (var carCol in carColliders)
                if (ballCol != null && carCol != null)
                    Physics.IgnoreCollision(ballCol, carCol, ignore);
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

        // Periodic diagnostic dump for tuning forward compensation
        if (Time.unscaledTime >= _nextDiagLogTime)
        {
            _nextDiagLogTime = Time.unscaledTime + DIAG_LOG_INTERVAL;
            float compMs = _smoothedSnapshotAgeSec * compensationFraction * 1000f;
            float ageMs = _smoothedSnapshotAgeSec * 1000f;
            float jitterMs = _arrivalJitter * 1000f;
            Debug.Log($"[Ball] comp={compMs:F1}ms age={ageMs:F1}ms jitter={jitterMs:F1}ms " +
                      $"extrap={_debugExtrapolationCount} snaps={_snapshots.Count} " +
                      $"backTime={DebugAdaptiveBackTimeMs:F1}ms pos={transform.position:F2} " +
                      $"vel={(_snapshots.Count > 0 ? _snapshots[_snapshots.Count - 1].State.Velocity.magnitude : 0f):F1}");
        }

        if (!_hasInterpTarget) return;

        if (!TrySampleSnapshot(Time.unscaledTime, out BallStatePayload from, out BallStatePayload to, out float t, out float extraTime, out bool extrapolate))
            return;

        if (!extrapolate)
        {
            _isExtrapolating = false;
            float segmentDuration = GetSnapshotSegmentDuration(from.Tick, to.Tick);

            Vector3 p0 = from.Position;
            Vector3 p1 = to.Position;
            Vector3 m0 = from.Velocity * segmentDuration;
            Vector3 m1 = to.Velocity * segmentDuration;

            transform.position = HermitePosition(t, p0, p1, m0, m1);
            transform.rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t);
            ApplyForwardCompensation(Vector3.Lerp(from.Velocity, to.Velocity, t));
            return;
        }

        if (extraTime <= _maxExtrapolationTime)
        {
            if (!_isExtrapolating)
                _debugExtrapolationCount++;
            _isExtrapolating = true;

            // Extrapolate with velocity + gravity for ballistic arc
            transform.position = to.Position
                + to.Velocity * extraTime
                + 0.5f * BallGravity * extraTime * extraTime;
            ApplyForwardCompensation(to.Velocity + BallGravity * extraTime);

            // Rotation extrapolation using angular velocity
            Vector3 angVel = to.AngularVelocity;
            if (angVel.sqrMagnitude > 0.001f)
            {
                float angSpeed = angVel.magnitude;
                Quaternion extraRot = Quaternion.AngleAxis(
                    angSpeed * Mathf.Rad2Deg * extraTime,
                    angVel.normalized
                );
                transform.rotation = extraRot * to.Rotation;
            }
        }
        // else: cap reached, hold latest rendered pose
    }

    /// <summary>
    /// Nudge transform.position forward along the current velocity by the smoothed
    /// compensation amount. Called right after each transform.position assignment
    /// in LateUpdate. No-op when forwardCompensation is off or age is negligible.
    /// </summary>
    void ApplyForwardCompensation(Vector3 currentVelocity)
    {
        if (!forwardCompensation || _smoothedSnapshotAgeSec < 0.001f) return;
        float dt = Mathf.Min(_smoothedSnapshotAgeSec * compensationFraction, maxCompensationTime);
        transform.position += currentVelocity * dt + 0.5f * BallGravity * dt * dt;
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

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    void BroadcastBallStateClientRpc(BallStatePayload state)
    {
        if (IsServer) return; // Server doesn't need to interpolate
        PushSnapshot(state);
    }

    /// <summary>
    /// Ballistic forward projection: advances a ball state by dt seconds using
    /// velocity + gravity (parabolic arc) and angular velocity for rotation.
    /// Used by forward temporal compensation to "un-age" stale snapshots.
    /// </summary>
    static BallStatePayload ProjectForward(BallStatePayload src, float dt)
    {
        BallStatePayload projected = src;

        // Parabolic position: p' = p + v*dt + 0.5*g*dt^2
        projected.Position = src.Position
            + src.Velocity * dt
            + 0.5f * BallGravity * dt * dt;

        // Velocity after gravity: v' = v + g*dt
        projected.Velocity = src.Velocity + BallGravity * dt;

        // Rotation from angular velocity
        Vector3 angVel = src.AngularVelocity;
        if (angVel.sqrMagnitude > 0.001f)
        {
            float angSpeed = angVel.magnitude;
            Quaternion deltaRot = Quaternion.AngleAxis(
                angSpeed * Mathf.Rad2Deg * dt,
                angVel.normalized
            );
            projected.Rotation = deltaRot * src.Rotation;
        }
        // AngularVelocity stays the same (no angular drag in projection)

        return projected;
    }

    /// <summary>
    /// Measures how "old" a snapshot is by comparing its tick to the client's
    /// estimate of the current server tick. Returns age in seconds.
    /// </summary>
    float MeasureSnapshotAge(int snapshotTick)
    {
        var tickSystem = NetworkManager.Singleton?.NetworkTickSystem;
        if (tickSystem == null) return 0f;

        // ServerTime.Tick = client's best estimate of what tick the server is on RIGHT NOW.
        // snapshot.Tick = the tick the server was on when it captured this state.
        // Difference = how many ticks have elapsed since the snapshot was captured.
        int currentServerTick = tickSystem.ServerTime.Tick;
        int ageTicks = currentServerTick - snapshotTick;

        if (ageTicks <= 0) return 0f; // snapshot is from the future or current — no compensation

        return ageTicks * GetNetworkTickDeltaTime();
    }

    void PushSnapshot(BallStatePayload state)
    {
        if (state.Tick <= _latestSnapshotTick)
            return;

        float now = Time.unscaledTime;
        if (_lastSnapshotArrivalTime > 0f)
        {
            float interval = now - _lastSnapshotArrivalTime;
            float expectedInterval = GetNetworkTickDeltaTime() * stateSendInterval;
            float jitter = Mathf.Abs(interval - expectedInterval);
            _arrivalJitter = Mathf.Lerp(_arrivalJitter, jitter, 0.1f);

            float targetBackTime = interpolationBackTimeMs + _arrivalJitter * 2000f;
            _adaptiveBackTimeMs = Mathf.Lerp(_adaptiveBackTimeMs, targetBackTime, 0.1f);
            _adaptiveBackTimeMs = Mathf.Clamp(_adaptiveBackTimeMs, interpolationBackTimeMs, 160f);
        }
        _lastSnapshotArrivalTime = now;

        // ── Forward Temporal Compensation (measurement only) ──
        // Measure snapshot age and smooth it with EMA. The actual forward projection
        // is applied uniformly in LateUpdate AFTER interpolation, so all snapshots
        // in the buffer stay in their original server-truth coordinates.
        // This prevents jitter from each snapshot being projected by a different amount.
        if (forwardCompensation)
        {
            float rawAge = MeasureSnapshotAge(state.Tick);
            _smoothedSnapshotAgeSec = Mathf.Lerp(_smoothedSnapshotAgeSec, rawAge, 0.08f);
            _compensatedSnapshotCount++;
        }

        _latestSnapshotTick = state.Tick;
        _snapshots.Add(new TimedBallSnapshot
        {
            State = state,
            ArrivalTime = now
        });

        while (_snapshots.Count > Mathf.Max(2, maxSnapshots))
            _snapshots.RemoveAt(0);

        _hasInterpTarget = _snapshots.Count > 0;
        _isExtrapolating = false;
    }

    bool TrySampleSnapshot(
        float now,
        out BallStatePayload from,
        out BallStatePayload to,
        out float t,
        out float extraTime,
        out bool extrapolate)
    {
        from = default;
        to = default;
        t = 0f;
        extraTime = 0f;
        extrapolate = false;

        if (_snapshots.Count == 0)
            return false;

        float renderTime = now - Mathf.Max(0.001f, DebugAdaptiveBackTimeMs * 0.001f);

        while (_snapshots.Count >= 2 && _snapshots[1].ArrivalTime <= renderTime)
            _snapshots.RemoveAt(0);

        if (_snapshots.Count >= 2)
        {
            TimedBallSnapshot a = _snapshots[0];
            TimedBallSnapshot b = _snapshots[1];

            if (renderTime <= b.ArrivalTime)
            {
                from = a.State;
                to = b.State;
                float denom = Mathf.Max(0.001f, b.ArrivalTime - a.ArrivalTime);
                t = Mathf.Clamp01((renderTime - a.ArrivalTime) / denom);
                return true;
            }
        }

        TimedBallSnapshot latest = _snapshots[_snapshots.Count - 1];
        from = latest.State;
        to = latest.State;
        extrapolate = true;
        extraTime = Mathf.Max(0f, renderTime - latest.ArrivalTime);
        return true;
    }

    float GetSnapshotSegmentDuration(int fromTick, int toTick)
    {
        int tickDelta = Mathf.Max(1, toTick - fromTick);
        return Mathf.Max(tickDelta * GetNetworkTickDeltaTime(), 0.001f);
    }

    float GetNetworkTickDeltaTime()
    {
        var singleton = NetworkManager.Singleton;
        if (singleton != null && singleton.NetworkConfig != null && singleton.NetworkConfig.TickRate > 0)
            return 1f / singleton.NetworkConfig.TickRate;

        return Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : (1f / 60f);
    }
}
