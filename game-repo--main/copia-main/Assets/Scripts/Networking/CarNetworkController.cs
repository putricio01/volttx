using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Core network controller for car physics authority.
/// Handles: server-side authoritative physics, client-side prediction, and server reconciliation.
/// Must be added to the car prefab alongside existing physics components.
/// Runs BEFORE all other physics scripts via execution order.
///
/// Uses NetworkTickSystem for synchronized ticks between server and client.
/// Both sides share the same tick numbering, so buffer lookups and
/// reconciliation comparisons always match.
///
/// Uses runtime IsServer/IsOwner checks instead of #if UNITY_SERVER so it works
/// in both MPPM editor testing and dedicated server builds.
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(CubeController))]
[RequireComponent(typeof(InputManager))]
[RequireComponent(typeof(TickRunner))]
public class CarNetworkController : NetworkBehaviour
{
    const int BUFFER_SIZE = 1024;
    const int DEFAULT_REMOTE_SNAPSHOT_BUFFER = 32;

    struct TimedRemoteCarSnapshot
    {
        public StatePayload State;
        public float ArrivalTime;
    }

    // Reconciliation thresholds — wider to reduce micro-corrections on mobile.
    // Predictive inputs still diverge a bit under touch and variable mobile frame pacing,
    // so we tolerate small drift and correct visually instead of constantly resnapping.
    [Header("Reconciliation Settings")]
    [SerializeField] float positionErrorThreshold = 0.18f;
    [SerializeField] float rotationErrorThreshold = 3.5f;
    [SerializeField] float hardSnapThreshold = 4.5f;

    // State send rate (server sends every N physics ticks)
    [SerializeField, Min(1)] int stateSendInterval = 1;

    // Circular buffers
    InputPayload[] _inputBuffer = new InputPayload[BUFFER_SIZE];
    StatePayload[] _stateBuffer = new StatePayload[BUFFER_SIZE];

    // Server: input queue from client
    InputPayload[] _serverInputBuffer = new InputPayload[BUFFER_SIZE];
    int _serverLastReceivedTick = -1;
    InputPayload _lastAppliedInput;
    float _lastInputReceivedTime;

    // Server: stale-input grace period — don't decay analog inputs until
    // this many consecutive ticks have passed without fresh input.
    // During the grace window the last received input is replayed as-is,
    // matching the client's own prediction and avoiding unnecessary divergence.
    [Header("Server Stale-Input Handling")]
    [SerializeField, Range(0, 12)] int staleInputGraceTicks = 4;
    int _serverConsecutiveMissedTicks = 0;

    // Diagnostics: how often is the server falling back to stale input?
    int _debugFallbackTickCount = 0;
    int _debugTotalServerTicks = 0;

    // Client: latest server state for reconciliation
    StatePayload _latestServerState;
    bool _hasNewServerState = false;
    int _lastServerStateTick = -1;
    int _lastSentInputTick = -1;

    // Tick tracking — uses NetworkTickSystem for synchronized ticks
    int _currentTick = 0;
    int _lastProcessedTick = -1;
    int _ticksSinceLastSend = 0;

    // Client: throttle input sends
    [SerializeField, Min(1)] int inputSendInterval = 1;
    [SerializeField, Range(1, InputBatchPayload.MaxInputs)] int inputRedundancyCount = 8;
    [SerializeField, Min(0)] int maxFutureInputTicks = 8;
    int _ticksSinceLastInputSend = 0;

    // Component references
    Rigidbody _rb;
    InputManager _inputManager;
    CubeController _controller;
    TickRunner _tickRunner;

    // Interpolation buffer for remote cars (non-owner clients)
    [Header("Remote Snapshot Interpolation")]
    [SerializeField, Range(20f, 250f)] float interpolationBackTimeMs = 75f;
    [SerializeField, Min(2)] int maxRemoteSnapshots = DEFAULT_REMOTE_SNAPSHOT_BUFFER;
    readonly List<TimedRemoteCarSnapshot> _remoteSnapshots = new List<TimedRemoteCarSnapshot>(DEFAULT_REMOTE_SNAPSHOT_BUFFER);
    int _latestRemoteSnapshotTick = -1;
    bool _hasInterpTarget = false;

    // Adaptive interpolation: adjusts backtime based on jitter
    float _adaptiveBackTimeMs;
    float _lastSnapshotArrivalTime;
    float _arrivalJitter;

    // Extrapolation — soft deceleration instead of hard freeze
    float _maxExtrapolationTime = 0.2f; // allow up to 200ms
    bool _isExtrapolating = false;

    // Flag to prevent trigger side effects during resimulation
    public static bool IsResimulating { get; private set; }

    // Visual smoothing: offsets the rendered position from physics to hide reconciliation snaps.
    // Physics state (_rb) is always fully authoritative — only the visual (transform) is offset.
    [Header("Owner Visual Smoothing")]
    [SerializeField, Range(0.03f, 0.2f)] float ownerCorrectionSmoothingTime = 0.08f;

    [Header("Camera Decoupling")]
    [Tooltip("Assign a child transform (e.g. CameraAnchor) for Cinemachine to follow. " +
             "This transform is positioned at the physics-truth pose each frame, " +
             "so the camera does not inherit reconciliation smoothing drift.")]
    [SerializeField] Transform cameraFollowAnchor;
    Vector3 _smoothingPosOffset;
    Quaternion _smoothingRotOffset = Quaternion.identity;
    Vector3 _appliedPosOffset;
    Quaternion _appliedRotOffset = Quaternion.identity;
    bool _smoothingApplied;

    // Impact freeze: temporarily boosts smoothing after a ball hit so the
    // reconciliation snap (client didn't predict ball collision) is absorbed.
    [Header("Ball Impact Freeze")]
    [SerializeField, Range(0.03f, 0.15f)] float impactFreezeDuration = 0.06f;
    [SerializeField, Range(0.1f, 0.5f)] float impactSmoothingTime = 0.25f;
    float _impactFreezeUntil;
    Vector3 _impactFreezePos;

    // Render interpolation: stores physics results of the two most recent ticks
    // so LateUpdate can blend smoothly between them at render framerate.
    // This eliminates the "stepped" motion that occurs when rendering at the same
    // rate as physics ticks (60Hz) or when frames don't align with FixedUpdate.
    Vector3 _renderInterpFrom;
    Vector3 _renderInterpTo;
    Quaternion _renderInterpRotFrom;
    Quaternion _renderInterpRotTo;
    bool _renderInterpInitialized;

    // Diagnostics
    float _lastDiagLogTime;
    bool _remoteModeApplied;
    bool _ownerModeApplied;
    bool _loggedResimFixedUpdateWarning;
    int _debugCorrectionCount;
    int _debugHardSnapCount;
    float _debugLastPosError;
    float _debugLastRotError;

    public int DebugCorrectionCount => _debugCorrectionCount;
    public int DebugHardSnapCount => _debugHardSnapCount;
    public float DebugLastPosError => _debugLastPosError;
    public float DebugLastRotError => _debugLastRotError;
    public float DebugAdaptiveBackTimeMs => _adaptiveBackTimeMs > 0f ? _adaptiveBackTimeMs : interpolationBackTimeMs;
    public float DebugArrivalJitterMs => _arrivalJitter * 1000f;
    public float DebugVisualSmoothingOffset => _smoothingPosOffset.magnitude;
    public bool DebugIsExtrapolating => _isExtrapolating;
    public int DebugFallbackTickCount => _debugFallbackTickCount;
    public int DebugTotalServerTicks => _debugTotalServerTicks;
    public float DebugFallbackPercent => _debugTotalServerTicks > 0 ? 100f * _debugFallbackTickCount / _debugTotalServerTicks : 0f;

    /// <summary>
    /// Read the synchronized tick from Netcode's NetworkTickSystem.
    /// </summary>
    int GetSyncedTick()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkTickSystem == null)
            return _currentTick;

        return NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick;
    }

    public override void OnNetworkSpawn()
    {
        _rb = GetComponent<Rigidbody>();
        _inputManager = GetComponent<InputManager>();
        _controller = GetComponent<CubeController>();
        _tickRunner = GetComponent<TickRunner>();
        if (_tickRunner == null)
            _tickRunner = gameObject.AddComponent<TickRunner>();
        _tickRunner.Refresh();

        _adaptiveBackTimeMs = interpolationBackTimeMs;
        _debugCorrectionCount = 0;
        _debugHardSnapCount = 0;
        _debugLastPosError = 0f;
        _debugLastRotError = 0f;
        _debugFallbackTickCount = 0;
        _debugTotalServerTicks = 0;
        _serverConsecutiveMissedTicks = 0;
        _renderInterpInitialized = false;

        LogNetworkConfig();

        if (IsServer)
        {
            _inputManager.serverMode = true;
            Debug.Log($"[Server] CarNetworkController spawned for client {OwnerClientId}");
        }

        if (IsClient)
        {
            _remoteSnapshots.Clear();
            _latestRemoteSnapshotTick = -1;
            _hasInterpTarget = false;

            if (IsOwner)
            {
                _inputManager.serverMode = false;
                SetupBallCollisionIgnore();
                Debug.Log("[Client] Own car spawned — prediction enabled");
            }
            else
            {
                Debug.Log("[Client] Remote car spawned — interpolation mode");
                _rb.isKinematic = true;
                DisablePhysicsScripts();
            }
        }

        EnsureClientAuthorityState(logIfFixed: false);
    }

    void FixedUpdate()
    {
        // VISUAL SMOOTHING: Undo the exact visual offset that LateUpdate baked into the transform.
        if (_smoothingApplied && IsClient && IsOwner)
        {
            transform.position -= _appliedPosOffset;
            transform.rotation = Quaternion.Inverse(_appliedRotOffset) * transform.rotation;
            _appliedPosOffset = Vector3.zero;
            _appliedRotOffset = Quaternion.identity;
            _smoothingApplied = false;
        }

        EnsureClientAuthorityState(logIfFixed: true);

        _currentTick = GetSyncedTick();

        if (_currentTick == _lastProcessedTick)
            return;
        _lastProcessedTick = _currentTick;

        if (IsServer)
        {
            ServerFixedUpdate();
        }

        if (IsClient && IsOwner)
        {
            ClientOwnerFixedUpdate();
        }

        DiagLog();
    }

    public override void OnGainedOwnership()
    {
        EnsureClientAuthorityState(logIfFixed: true);
    }

    public override void OnLostOwnership()
    {
        EnsureClientAuthorityState(logIfFixed: true);
    }

    // ========================================================================
    // SERVER LOGIC
    // ========================================================================

    void ServerFixedUpdate()
    {
        // Only count ticks after the first input has arrived.
        // Before that, the client is still connecting/loading and every tick
        // would be a "fallback", inflating the diagnostic percentage.
        if (_serverLastReceivedTick >= 0)
            _debugTotalServerTicks++;

        int bufferIndex = _currentTick % BUFFER_SIZE;
        if (_serverInputBuffer[bufferIndex].Tick == _currentTick)
        {
            // Fresh input for this exact tick — use it and reset grace counter.
            _lastAppliedInput = _serverInputBuffer[bufferIndex];
            _serverConsecutiveMissedTicks = 0;
        }
        else if (_serverLastReceivedTick >= 0)
        {
            // Input hasn't arrived for this tick — replay last received input.
            // During the grace window we replay it unmodified so the server
            // matches the client's own prediction (which used the same input).
            // After the grace period, decay analog values to avoid holding
            // stale throttle/steer indefinitely on real disconnects.
            _serverConsecutiveMissedTicks++;
            _debugFallbackTickCount++;

            int lastIndex = _serverLastReceivedTick % BUFFER_SIZE;
            if (_serverInputBuffer[lastIndex].Tick == _serverLastReceivedTick)
            {
                _lastAppliedInput = _serverInputBuffer[lastIndex];

                if (_serverConsecutiveMissedTicks > staleInputGraceTicks)
                {
                    _lastAppliedInput.ThrottleInput *= 0.8f;
                    _lastAppliedInput.SteerInput *= 0.8f;
                    _lastAppliedInput.YawInput *= 0.8f;
                }
            }
        }

        if (_serverLastReceivedTick >= 0)
        {
            _inputManager.ApplyInputPayload(_lastAppliedInput);
        }
    }

    public void OnPostPhysics()
    {
        if (!IsServer) return;

        _ticksSinceLastSend++;

        if (_ticksSinceLastSend >= stateSendInterval)
        {
            _ticksSinceLastSend = 0;

            StatePayload state = _controller.CaptureStatePayload(_currentTick);
            _stateBuffer[_currentTick % BUFFER_SIZE] = state;

            SendStateToClientRpc(state, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { OwnerClientId }
                }
            });

            BroadcastPositionClientRpc(
                state.Tick,
                state.Position,
                state.Rotation,
                state.Velocity,
                state.AngularVelocity
            );
        }
    }

    public void OnPostPhysicsClient()
    {
        if (!IsClient || !IsOwner) return;

        _stateBuffer[_currentTick % BUFFER_SIZE] = _controller.CaptureStatePayload(_currentTick);

        if (_hasNewServerState)
        {
            _hasNewServerState = false;
            Reconcile(_latestServerState);
        }
    }

    [ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Unreliable)]
    void SendInputBatchToServerRpc(InputBatchPayload batch)
    {
        int serverTickNow = GetSyncedTick();
        int minAcceptedTick = Mathf.Max(0, serverTickNow - BUFFER_SIZE + 1);
        int maxAcceptedTick = serverTickNow + maxFutureInputTicks;
        bool acceptedAny = false;

        int count = Mathf.Clamp(batch.Count, 0, InputBatchPayload.MaxInputs);
        for (int i = 0; i < count; i++)
        {
            InputPayload input = batch.GetAt(i);
            input.ClampAnalogInputs();

            if (input.Tick < minAcceptedTick || input.Tick > maxAcceptedTick)
                continue;

            _serverInputBuffer[input.Tick % BUFFER_SIZE] = input;
            if (input.Tick > _serverLastReceivedTick)
                _serverLastReceivedTick = input.Tick;

            acceptedAny = true;
        }

        if (acceptedAny)
            _lastInputReceivedTime = Time.time;
    }

    // ========================================================================
    // CLIENT OWNER LOGIC (Prediction + Reconciliation)
    // ========================================================================

    void ClientOwnerFixedUpdate()
    {
        InputPayload input = _inputManager.CaptureInputPayload(_currentTick);
        _inputBuffer[_currentTick % BUFFER_SIZE] = input;
        _lastSentInputTick = input.Tick;

        _ticksSinceLastInputSend++;
        if (_ticksSinceLastInputSend >= inputSendInterval)
        {
            _ticksSinceLastInputSend = 0;
            SendInputBatchToServerRpc(BuildInputBatch());
        }
    }

    /// <summary>
    /// Compare server state with our prediction. If they diverge beyond threshold,
    /// rewind and resimulate.
    /// </summary>
    void Reconcile(StatePayload serverState)
    {
        int serverTick = serverState.Tick;

        // Guard: reject out-of-order states (UDP can deliver them out of sequence)
        if (serverTick <= _lastServerStateTick - 1 && _lastServerStateTick > 0)
            return;

        int bufferIndex = serverTick % BUFFER_SIZE;
        StatePayload predictedState = _stateBuffer[bufferIndex];

        if (predictedState.Tick != serverTick) return;

        float posError = Vector3.Distance(serverState.Position, predictedState.Position);
        float rotError = Quaternion.Angle(serverState.Rotation, predictedState.Rotation);
        _debugLastPosError = posError;
        _debugLastRotError = rotError;

        if (posError <= positionErrorThreshold && rotError <= rotationErrorThreshold)
            return;

        _debugCorrectionCount++;

        // Save pre-resim state for visual offset calculation
        StatePayload preResimCurrentState = _stateBuffer[_currentTick % BUFFER_SIZE];

        // REWIND + RESIMULATE
        _controller.RestoreStatePayload(serverState);

        if (!_loggedResimFixedUpdateWarning)
        {
            _loggedResimFixedUpdateWarning = true;
            Debug.Log("[Netcode] Reconciliation resimulation using TickRunner.");
        }

        IsResimulating = true;
        var prevSimMode = Physics.simulationMode;
        bool prevAutoSim = Physics.autoSimulation;
        Physics.autoSimulation = false;
        Physics.simulationMode = SimulationMode.Script;

        for (int tick = serverTick + 1; tick <= _currentTick; tick++)
        {
            int idx = tick % BUFFER_SIZE;
            InputPayload replayInput = _inputBuffer[idx];

            if (replayInput.Tick == tick)
            {
                _inputManager.ApplyInputPayload(replayInput);
            }

            _tickRunner.RunPrePhysicsTick();
            Physics.Simulate(Time.fixedDeltaTime);
            _stateBuffer[idx] = _controller.CaptureStatePayload(tick);
        }

        Physics.simulationMode = prevSimMode;
        Physics.autoSimulation = prevAutoSim;
        IsResimulating = false;

        Vector3 resimPos = _rb.position;
        Quaternion resimRot = _rb.rotation;

        if (posError > hardSnapThreshold)
        {
            _debugHardSnapCount++;
            _smoothingPosOffset = Vector3.zero;
            _smoothingRotOffset = Quaternion.identity;
            return;
        }

        // Accumulate visual error offset
        _smoothingPosOffset += preResimCurrentState.Position - resimPos;
        _smoothingRotOffset = (preResimCurrentState.Rotation * Quaternion.Inverse(resimRot)) * _smoothingRotOffset;

        const float MAX_SMOOTHING_OFFSET = 1f;
        if (_smoothingPosOffset.sqrMagnitude > MAX_SMOOTHING_OFFSET * MAX_SMOOTHING_OFFSET)
            _smoothingPosOffset = Vector3.ClampMagnitude(_smoothingPosOffset, MAX_SMOOTHING_OFFSET);
    }

    // ========================================================================
    // REMOTE CAR INTERPOLATION
    // ========================================================================

    void LateUpdate()
    {
        if (!IsClient) return;

        // ── OWNER: apply visual smoothing offset ──
        if (IsOwner && !IsServer)
        {
            // Undo previously applied offset → transform is now at physics truth
            if (_smoothingApplied)
            {
                transform.position -= _appliedPosOffset;
                transform.rotation = Quaternion.Inverse(_appliedRotOffset) * transform.rotation;
            }

            // Capture physics-truth pose BEFORE re-applying smoothing.
            // The camera anchor will track this so Cinemachine doesn't inherit drift.
            Vector3 physicsTruthPos = transform.position;
            Quaternion physicsTruthRot = transform.rotation;

            // ── Impact freeze: hold visual pose briefly after ball hit ──
            // During freeze, we lerp slowly from the frozen pose toward physics truth
            // instead of snapping. This absorbs the reconciliation correction.
            if (Time.time < _impactFreezeUntil)
            {
                float freezeT = 1f - ((Time.time - (_impactFreezeUntil - impactFreezeDuration)) / impactFreezeDuration);
                freezeT = Mathf.Clamp01(freezeT);
                // Blend: at start of freeze (freezeT=1) hold frozen pose,
                // as freeze expires (freezeT→0) converge to physics truth
                float holdFactor = freezeT * freezeT; // ease-out curve
                transform.position = Vector3.Lerp(physicsTruthPos, _impactFreezePos, holdFactor);
                // Keep the authoritative rotation during ball-hit freeze so we
                // never replay a client-only pitch/roll tilt after reconciliation.
                transform.rotation = physicsTruthRot;
                _appliedPosOffset = transform.position - physicsTruthPos;
                _appliedRotOffset = Quaternion.identity;
                _smoothingApplied = true;
                // Reset smoothing offset so it doesn't stack on top of freeze
                _smoothingPosOffset = Vector3.zero;
                _smoothingRotOffset = Quaternion.identity;

                if (cameraFollowAnchor != null)
                {
                    cameraFollowAnchor.position = physicsTruthPos;
                    cameraFollowAnchor.rotation = physicsTruthRot;
                }
                return;
            }

            // Fixed-duration decay: shorter convergence reduces the "dragging behind" feel on mobile owners.
            float decay = 1f - Mathf.Pow(0.01f, Time.deltaTime / ownerCorrectionSmoothingTime);
            _smoothingPosOffset = Vector3.Lerp(_smoothingPosOffset, Vector3.zero, decay);
            _smoothingRotOffset = Quaternion.Slerp(_smoothingRotOffset, Quaternion.identity, decay);

            // Zero out negligible offsets
            if (_smoothingPosOffset.sqrMagnitude < 0.0001f)
                _smoothingPosOffset = Vector3.zero;
            if (Quaternion.Angle(_smoothingRotOffset, Quaternion.identity) < 0.01f)
                _smoothingRotOffset = Quaternion.identity;

            // Apply offset to visual transform
            if (_smoothingPosOffset.sqrMagnitude > 0f || _smoothingRotOffset != Quaternion.identity)
            {
                transform.position += _smoothingPosOffset;
                transform.rotation = _smoothingRotOffset * transform.rotation;
                _appliedPosOffset = _smoothingPosOffset;
                _appliedRotOffset = _smoothingRotOffset;
                _smoothingApplied = true;
            }
            else
            {
                _appliedPosOffset = Vector3.zero;
                _appliedRotOffset = Quaternion.identity;
                _smoothingApplied = false;
            }

            // Position camera anchor at physics truth so Cinemachine
            // follows the stable, un-smoothed pose (no reconciliation drift).
            if (cameraFollowAnchor != null)
            {
                cameraFollowAnchor.position = physicsTruthPos;
                cameraFollowAnchor.rotation = physicsTruthRot;
            }

            return;
        }

        // ── REMOTE: interpolation from snapshot buffer ──
        if (IsOwner || IsServer) return;

        if (!_hasInterpTarget) return;

        if (!TrySampleRemoteSnapshot(Time.unscaledTime, out StatePayload from, out StatePayload to, out float t, out float extraTime, out bool extrapolate))
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
            return;
        }

        // Soft extrapolation: decelerate over time instead of hard freeze
        _isExtrapolating = true;
        float decelFactor = Mathf.Clamp01(1f - (extraTime / _maxExtrapolationTime));
        transform.position = to.Position + to.Velocity * extraTime * decelFactor;

        Vector3 angVel = to.AngularVelocity;
        if (angVel.sqrMagnitude > 0.001f)
        {
            float angSpeed = angVel.magnitude;
            Quaternion extraRot = Quaternion.AngleAxis(
                angSpeed * Mathf.Rad2Deg * extraTime * decelFactor,
                angVel.normalized
            );
            transform.rotation = extraRot * to.Rotation;
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

    // ========================================================================
    // RPC METHODS
    // ========================================================================

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    void SendStateToClientRpc(StatePayload state, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        // Guard: reject out-of-order states
        if (state.Tick <= _lastServerStateTick)
            return;

        _latestServerState = state;
        _hasNewServerState = true;
        _lastServerStateTick = state.Tick;
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    void NotifyBallImpactClientRpc(ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        // Snapshot current visual pose and enter freeze — LateUpdate will hold
        // this pose briefly, giving the reconciliation smoothing time to absorb
        // the server correction from the ball collision the client didn't predict.
        _impactFreezeUntil = Time.time + impactFreezeDuration;
        _impactFreezePos = transform.position;
    }

    /// <summary>
    /// Called by Ball.cs on the server when this car collides with the ball.
    /// Sends the impact freeze hint to the owning client.
    /// </summary>
    public void ServerNotifyBallImpact()
    {
        if (!IsServer) return;
        NotifyBallImpactClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        });
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    void BroadcastPositionClientRpc(int tick, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
    {
        if (IsOwner || IsServer) return;

        PushRemoteSnapshot(new StatePayload
        {
            Tick = tick,
            Position = position,
            Rotation = rotation,
            Velocity = velocity,
            AngularVelocity = angularVelocity
        });
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    InputBatchPayload BuildInputBatch()
    {
        var batch = new InputBatchPayload
        {
            Count = 0,
            LatestServerStateAckTick = _lastServerStateTick
        };

        int requestedCount = Mathf.Clamp(inputRedundancyCount, 1, InputBatchPayload.MaxInputs);
        for (int delta = requestedCount - 1; delta >= 0; delta--)
        {
            int tick = _currentTick - delta;
            if (tick < 0)
                continue;

            InputPayload payload = _inputBuffer[tick % BUFFER_SIZE];
            if (payload.Tick != tick)
                continue;

            payload.ClampAnalogInputs();
            batch.SetAt(batch.Count, payload);
            batch.Count++;
        }

        return batch;
    }

    void PushRemoteSnapshot(StatePayload state)
    {
        if (state.Tick <= _latestRemoteSnapshotTick)
            return;

        // Adaptive interpolation: measure arrival jitter and adjust backtime
        float now = Time.unscaledTime;
        if (_lastSnapshotArrivalTime > 0f)
        {
            float interval = now - _lastSnapshotArrivalTime;
            float expectedInterval = GetNetworkTickDeltaTime() * stateSendInterval;
            float jitter = Mathf.Abs(interval - expectedInterval);
            _arrivalJitter = Mathf.Lerp(_arrivalJitter, jitter, 0.1f);

            // Adapt: backtime = base + 2x jitter (clamped)
            float targetBackTime = interpolationBackTimeMs + _arrivalJitter * 2000f;
            _adaptiveBackTimeMs = Mathf.Lerp(_adaptiveBackTimeMs, targetBackTime, 0.1f);
            _adaptiveBackTimeMs = Mathf.Clamp(_adaptiveBackTimeMs, interpolationBackTimeMs, 200f);
        }
        _lastSnapshotArrivalTime = now;

        _latestRemoteSnapshotTick = state.Tick;
        _remoteSnapshots.Add(new TimedRemoteCarSnapshot
        {
            State = state,
            ArrivalTime = now
        });

        int maxSnapshots = Mathf.Max(2, maxRemoteSnapshots);
        while (_remoteSnapshots.Count > maxSnapshots)
            _remoteSnapshots.RemoveAt(0);

        _hasInterpTarget = _remoteSnapshots.Count > 0;
        _isExtrapolating = false;
    }

    bool TrySampleRemoteSnapshot(
        float now,
        out StatePayload from,
        out StatePayload to,
        out float t,
        out float extraTime,
        out bool extrapolate)
    {
        from = default;
        to = default;
        t = 0f;
        extraTime = 0f;
        extrapolate = false;

        if (_remoteSnapshots.Count == 0)
            return false;

        // Use adaptive backtime instead of fixed
        float renderTime = now - Mathf.Max(0.001f, _adaptiveBackTimeMs * 0.001f);

        while (_remoteSnapshots.Count >= 2 && _remoteSnapshots[1].ArrivalTime <= renderTime)
            _remoteSnapshots.RemoveAt(0);

        if (_remoteSnapshots.Count >= 2)
        {
            TimedRemoteCarSnapshot a = _remoteSnapshots[0];
            TimedRemoteCarSnapshot b = _remoteSnapshots[1];

            if (renderTime <= b.ArrivalTime)
            {
                from = a.State;
                to = b.State;

                float denom = Mathf.Max(0.001f, b.ArrivalTime - a.ArrivalTime);
                t = Mathf.Clamp01((renderTime - a.ArrivalTime) / denom);
                return true;
            }
        }

        TimedRemoteCarSnapshot latest = _remoteSnapshots[_remoteSnapshots.Count - 1];
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

    void DisablePhysicsScripts()
    {
        if (_controller) _controller.enabled = false;

        var groundControl = GetComponent<CubeGroundControl>();
        if (groundControl) groundControl.enabled = false;

        var airControl = GetComponent<CubeAirControl>();
        if (airControl) airControl.enabled = false;

        var jumping = GetComponent<CubeJumping>();
        if (jumping) jumping.enabled = false;

        var boosting = GetComponent<CubeBoosting>();
        if (boosting) boosting.enabled = false;

        var inputMgr = GetComponent<InputManager>();
        if (inputMgr) inputMgr.enabled = false;

        foreach (var wheel in GetComponentsInChildren<CubeWheel>())
            wheel.enabled = false;

        foreach (var sc in GetComponentsInChildren<CubeSphereCollider>())
            sc.enabled = false;
    }

    void EnablePhysicsScripts()
    {
        if (_controller) _controller.enabled = true;

        var groundControl = GetComponent<CubeGroundControl>();
        if (groundControl) groundControl.enabled = true;

        var airControl = GetComponent<CubeAirControl>();
        if (airControl) airControl.enabled = true;

        var jumping = GetComponent<CubeJumping>();
        if (jumping) jumping.enabled = true;

        var boosting = GetComponent<CubeBoosting>();
        if (boosting) boosting.enabled = true;

        var inputMgr = GetComponent<InputManager>();
        if (inputMgr) inputMgr.enabled = true;

        foreach (var wheel in GetComponentsInChildren<CubeWheel>())
            wheel.enabled = true;

        foreach (var sc in GetComponentsInChildren<CubeSphereCollider>())
            sc.enabled = true;
    }

    void EnsureClientAuthorityState(bool logIfFixed)
    {
        if (!IsClient || _rb == null) return;

        if (IsOwner)
        {
            if (_rb.isKinematic)
            {
                _rb.isKinematic = false;
                if (logIfFixed)
                    Debug.LogWarning("[Client] Owner car Rigidbody was kinematic. Restored dynamic mode.");
            }

            if (!_ownerModeApplied)
            {
                EnablePhysicsScripts();
                _ownerModeApplied = true;
                _remoteModeApplied = false;
            }

            if (_inputManager != null) _inputManager.serverMode = false;
            return;
        }

        if (!_rb.isKinematic)
        {
            _rb.isKinematic = true;
            if (logIfFixed)
                Debug.Log("[Client] Remote car switched to kinematic interpolation mode.");
        }

        if (!_remoteModeApplied)
        {
            DisablePhysicsScripts();
            _remoteModeApplied = true;
            _ownerModeApplied = false;
        }
    }

    public void ResetBuffers()
    {
        _inputBuffer = new InputPayload[BUFFER_SIZE];
        _stateBuffer = new StatePayload[BUFFER_SIZE];
        _serverInputBuffer = new InputPayload[BUFFER_SIZE];
        _hasNewServerState = false;
        _latestServerState = default;
        _lastServerStateTick = -1;
        _lastSentInputTick = -1;
        _serverLastReceivedTick = -1;
        _lastAppliedInput = default;
        _lastInputReceivedTime = 0f;
        _ticksSinceLastSend = 0;
        _ticksSinceLastInputSend = 0;
        _remoteSnapshots.Clear();
        _latestRemoteSnapshotTick = -1;
        _hasInterpTarget = false;
        _adaptiveBackTimeMs = interpolationBackTimeMs;
        _lastSnapshotArrivalTime = 0f;
        _arrivalJitter = 0f;
        _isExtrapolating = false;
        _smoothingPosOffset = Vector3.zero;
        _smoothingRotOffset = Quaternion.identity;
        _appliedPosOffset = Vector3.zero;
        _appliedRotOffset = Quaternion.identity;
        _smoothingApplied = false;
        _impactFreezeUntil = 0f;
    }

    void DiagLog()
    {
        if (Time.time - _lastDiagLogTime < 1f) return;
        _lastDiagLogTime = Time.time;

        if (IsClient && IsOwner)
        {
            float speed = _rb ? _rb.linearVelocity.magnitude : 0f;
            int serverTick = NetworkManager.Singleton?.NetworkTickSystem?.ServerTime.Tick ?? -1;
            Debug.Log($"[Diag][Client {OwnerClientId}] syncedTick={_currentTick} serverTick={serverTick} sentInput={_lastSentInputTick} lastServerState={_lastServerStateTick} hasNewState={_hasNewServerState} remoteBuf={_remoteSnapshots.Count} extrap={_isExtrapolating} pos={_rb.position} speed={speed:0.00} throttle={_inputManager?.throttleInput:0.00} steer={_inputManager?.steerInput:0.00} connected={NetworkManager.Singleton.IsConnectedClient} focused={Application.isFocused} kinematic={_rb.isKinematic} fixedDt={Time.fixedDeltaTime:0.0000} tickDt={GetNetworkTickDeltaTime():0.0000}");
        }

        if (IsServer && !IsOwner)
        {
            int inputLead = _serverLastReceivedTick - _currentTick; // positive = client ahead (good), negative = behind (bad)
            Debug.Log($"[Diag][Server view of client {OwnerClientId}] syncedTick={_currentTick} lastInputTick={_serverLastReceivedTick} lead={inputLead} lastAppliedTick={_lastAppliedInput.Tick} lastInputAge={(Time.time - _lastInputReceivedTime):0.00}s fallback={_debugFallbackTickCount}/{_debugTotalServerTicks} ({DebugFallbackPercent:0.0}%) missedStreak={_serverConsecutiveMissedTicks}");
        }
    }

    void LogNetworkConfig()
    {
        if (NetworkManager.Singleton == null) return;
        var cfg = NetworkManager.Singleton.NetworkConfig;
        ulong hash = cfg.GetConfig(false);
        int prefabLists = cfg.Prefabs?.NetworkPrefabsLists?.Count ?? 0;
        string role = IsServer ? "Server" : (IsClient ? (IsOwner ? "ClientOwner" : "ClientRemote") : "Unknown");
        Debug.Log($"[Diag][{role}] NetworkConfig hash={hash} tickRate={cfg.TickRate} prefabLists={prefabLists} forceSamePrefabs={cfg.ForceSamePrefabs}");

        if (cfg.TickRate > 0)
        {
            float tickDt = 1f / cfg.TickRate;
            float fixedDt = Time.fixedDeltaTime;
            if (Mathf.Abs(tickDt - fixedDt) > 0.0005f)
            {
                Debug.LogWarning($"[Netcode][{role}] TickRate ({cfg.TickRate}Hz -> {tickDt:0.0000}s) and Time.fixedDeltaTime ({fixedDt:0.0000}s) are not aligned! This causes jitter.");
            }
        }
    }

    // ========================================================================
    // BALL COLLISION IGNORE (Client Owner Only)
    // ========================================================================

    /// <summary>
    /// On the owner client, disable physics collision between this car and the ball.
    /// The ball is kinematic on clients, so without this the car prediction would
    /// collide with an immovable object (causing false tilt/snap on impact).
    /// The server handles the real ball-car collision with dynamic physics.
    /// </summary>
    void SetupBallCollisionIgnore()
    {
        // Server: also ignore physics collision so Unity doesn't apply reaction
        // forces that tilt cars. Ball.cs detects hits via OverlapSphere instead.
        if (IsServer)
        {
            SetupBallCollisionIgnoreForRole("Server");
            return;
        }

        // Client: ignore collision so owner car prediction doesn't hit kinematic ball.
        if (IsClient && IsOwner)
        {
            SetupBallCollisionIgnoreForRole("Client");
        }
    }

    void SetupBallCollisionIgnoreForRole(string role)
    {
        // On server, find ball directly (no singleton). On client, use Instance.
        BallNetworkController ball = IsServer
            ? FindFirstObjectByType<BallNetworkController>()
            : BallNetworkController.Instance;

        if (ball != null)
        {
            Collider[] carColliders = GetComponentsInChildren<Collider>();
            ball.IgnoreCollisionWithCar(carColliders);
            Debug.Log($"[{role}] Car ignoring ball collisions");
        }
        else
        {
            StartCoroutine(WaitForBallAndIgnoreCollision(role));
        }
    }

    System.Collections.IEnumerator WaitForBallAndIgnoreCollision(string role)
    {
        float timeout = 5f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            BallNetworkController ball = IsServer
                ? FindFirstObjectByType<BallNetworkController>()
                : BallNetworkController.Instance;

            if (ball != null)
            {
                Collider[] carColliders = GetComponentsInChildren<Collider>();
                ball.IgnoreCollisionWithCar(carColliders);
                Debug.Log($"[{role}] Car ignoring ball collisions (deferred)");
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning($"[{role}] Ball not found after timeout — collision ignore not set up");
    }
}
