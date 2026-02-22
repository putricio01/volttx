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
public class CarNetworkController : NetworkBehaviour
{
    const int BUFFER_SIZE = 1024;

    // Reconciliation thresholds — generous to avoid constant micro-corrections
    [Header("Reconciliation Settings")]
    [SerializeField] float positionErrorThreshold = 0.5f;   // only correct if >0.5m off
    [SerializeField] float rotationErrorThreshold = 5f;      // only correct if >5 degrees off
    [SerializeField] float hardSnapThreshold = 3f;           // hard snap if >3m off (respawn etc)
    [SerializeField] float correctionBlend = 0.7f;           // 0-1: how much of the correction to apply (1=full snap, 0.7=snappy)

    // State send rate (server sends every N physics ticks)
    [SerializeField, Min(1)] int stateSendInterval = 2; // send at 30Hz if physics is 60Hz

    // Circular buffers
    InputPayload[] _inputBuffer = new InputPayload[BUFFER_SIZE];
    StatePayload[] _stateBuffer = new StatePayload[BUFFER_SIZE];

    // Server: input queue from client
    InputPayload[] _serverInputBuffer = new InputPayload[BUFFER_SIZE];
    int _serverLastReceivedTick = -1;
    InputPayload _lastAppliedInput;
    float _lastInputReceivedTime;

    // Client: latest server state for reconciliation
    StatePayload _latestServerState;
    bool _hasNewServerState = false;
    int _lastServerStateTick = -1;
    int _lastSentInputTick = -1;

    // Tick tracking — uses NetworkTickSystem for synchronized ticks
    int _currentTick = 0;
    int _lastProcessedTick = -1; // guard against duplicate ticks in same FixedUpdate
    int _ticksSinceLastSend = 0;

    // Client: throttle input sends
    [SerializeField, Min(1)] int inputSendInterval = 2; // send inputs at 30Hz if physics is 60Hz
    int _ticksSinceLastInputSend = 0;

    // Component references
    Rigidbody _rb;
    InputManager _inputManager;
    CubeController _controller;

    // Interpolation buffer for remote cars (non-owner clients)
    StatePayload _interpFrom, _interpTo;
    float _interpTime = 0f;
    float _interpDuration = 0.1f;
    bool _hasInterpTarget = false;

    // Flag to prevent trigger side effects during resimulation
    public static bool IsResimulating { get; private set; }

    // Diagnostics
    float _lastDiagLogTime;
    bool _remoteModeApplied;
    bool _ownerModeApplied;

    /// <summary>
    /// Read the synchronized tick from Netcode's NetworkTickSystem.
    /// On the server, LocalTime and ServerTime are identical.
    /// On the client, LocalTime is the client's prediction of the server's current tick,
    /// adjusted for network latency — so both sides agree on what "tick N" means.
    /// </summary>
    int GetSyncedTick()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkTickSystem == null)
            return _currentTick; // fallback before network is ready

        // Use LocalTime.Tick — synchronized on both server and client
        return NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick;
    }

    public override void OnNetworkSpawn()
    {
        _rb = GetComponent<Rigidbody>();
        _inputManager = GetComponent<InputManager>();
        _controller = GetComponent<CubeController>();

        LogNetworkConfig();

        if (IsServer)
        {
            _inputManager.serverMode = true;
            Debug.Log($"[Server] CarNetworkController spawned for client {OwnerClientId}");
        }

        if (IsClient)
        {
            if (IsOwner)
            {
                _inputManager.serverMode = false;
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
        EnsureClientAuthorityState(logIfFixed: true);

        // Read synchronized tick from NetworkTickSystem instead of manual counter
        _currentTick = GetSyncedTick();

        // Guard: if the tick hasn't advanced since the last FixedUpdate, skip.
        // This can happen when FixedUpdate runs faster than the network tick rate.
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
        // Look up the client's input for the current (synchronized) tick.
        // Because both sides now use the same tick numbering, the buffer slot
        // should contain the matching input from the client.
        int bufferIndex = _currentTick % BUFFER_SIZE;
        if (_serverInputBuffer[bufferIndex].Tick == _currentTick)
        {
            _lastAppliedInput = _serverInputBuffer[bufferIndex];
        }
        else if (_serverLastReceivedTick >= 0)
        {
            // Fallback: input hasn't arrived yet (network jitter).
            // Repeat the most recent input to avoid freezing the car.
            int lastIndex = _serverLastReceivedTick % BUFFER_SIZE;
            if (_serverInputBuffer[lastIndex].Tick == _serverLastReceivedTick)
            {
                _lastAppliedInput = _serverInputBuffer[lastIndex];
            }
        }

        if (_lastAppliedInput.Tick > 0)
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
                state.Velocity
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

    [ServerRpc(RequireOwnership = false)]
    void SendInputToServerRpc(InputPayload input)
    {
        _serverInputBuffer[input.Tick % BUFFER_SIZE] = input;
        _lastInputReceivedTime = Time.time;

        if (input.Tick > _serverLastReceivedTick)
            _serverLastReceivedTick = input.Tick;
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
            SendInputToServerRpc(input);
        }
    }

    /// <summary>
    /// Compare server state with our prediction. If they diverge beyond threshold,
    /// rewind and resimulate, then BLEND toward the corrected position instead of snapping.
    /// Now that ticks are synchronized, the server's tick N and our tick N refer to the
    /// same simulation moment — so the comparison is always valid.
    /// </summary>
    void Reconcile(StatePayload serverState)
    {
        int serverTick = serverState.Tick;
        int bufferIndex = serverTick % BUFFER_SIZE;

        StatePayload predictedState = _stateBuffer[bufferIndex];

        if (predictedState.Tick != serverTick) return;

        float posError = Vector3.Distance(serverState.Position, predictedState.Position);
        float rotError = Quaternion.Angle(serverState.Rotation, predictedState.Rotation);

        // Within threshold — prediction is good, no correction needed
        if (posError <= positionErrorThreshold && rotError <= rotationErrorThreshold)
            return;

        // REWIND + RESIMULATE to get the "correct" position
        _controller.RestoreStatePayload(serverState);

        IsResimulating = true;
        var prevSimMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        for (int tick = serverTick + 1; tick <= _currentTick; tick++)
        {
            int idx = tick % BUFFER_SIZE;
            InputPayload replayInput = _inputBuffer[idx];

            if (replayInput.Tick == tick)
            {
                _inputManager.ApplyInputPayload(replayInput);
            }

            Physics.Simulate(Time.fixedDeltaTime);
            _stateBuffer[idx] = _controller.CaptureStatePayload(tick);
        }

        Physics.simulationMode = prevSimMode;
        IsResimulating = false;

        // After resim, _rb now holds the "correct" server-reconciled position.
        // Blend between where we WERE (predicted) and where we SHOULD BE (resimulated).

        Vector3 resimPos = _rb.position;
        Quaternion resimRot = _rb.rotation;
        Vector3 resimVel = _rb.linearVelocity;
        Vector3 resimAngVel = _rb.angularVelocity;

        if (posError > hardSnapThreshold)
        {
            // Huge error (respawn, teleport) — just snap, no blend
            return;
        }

        // Blend: correctionBlend=0.7 means apply 70% of the correction — snappy but smooth
        float blend = correctionBlend;

        _rb.position = Vector3.Lerp(predictedState.Position, resimPos, blend);
        _rb.rotation = Quaternion.Slerp(predictedState.Rotation, resimRot, blend);
        _rb.linearVelocity = Vector3.Lerp(predictedState.Velocity, resimVel, blend);
        _rb.angularVelocity = Vector3.Lerp(predictedState.AngularVelocity, resimAngVel, blend);
    }

    // ========================================================================
    // REMOTE CAR INTERPOLATION
    // ========================================================================

    void LateUpdate()
    {
        // Only non-owner clients interpolate remote cars
        if (!IsClient || IsOwner || IsServer) return;

        if (_hasInterpTarget)
        {
            _interpTime += Time.deltaTime;
            float t = Mathf.Clamp01(_interpTime / _interpDuration);

            transform.position = Vector3.Lerp(_interpFrom.Position, _interpTo.Position, t);
            transform.rotation = Quaternion.Slerp(_interpFrom.Rotation, _interpTo.Rotation, t);
        }
    }

    // ========================================================================
    // RPC METHODS
    // ========================================================================

    [ClientRpc]
    void SendStateToClientRpc(StatePayload state, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        _latestServerState = state;
        _hasNewServerState = true;
        _lastServerStateTick = state.Tick;
    }

    [ClientRpc]
    void BroadcastPositionClientRpc(int tick, Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        if (IsOwner || IsServer) return;

        _interpFrom = _interpTo;
        _interpTo = new StatePayload
        {
            Tick = tick,
            Position = position,
            Rotation = rotation,
            Velocity = velocity
        };
        _interpTime = 0f;
        _hasInterpTarget = true;

        if (_interpFrom.Tick > 0)
        {
            int tickDelta = _interpTo.Tick - _interpFrom.Tick;
            _interpDuration = Mathf.Max(tickDelta * Time.fixedDeltaTime, 0.033f);
        }
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

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
        _hasNewServerState = false;
    }

    void DiagLog()
    {
        // Log once per second to diagnose stalls.
        if (Time.time - _lastDiagLogTime < 1f) return;
        _lastDiagLogTime = Time.time;

        if (IsClient && IsOwner)
        {
            float speed = _rb ? _rb.linearVelocity.magnitude : 0f;
            int serverTick = NetworkManager.Singleton?.NetworkTickSystem?.ServerTime.Tick ?? -1;
            Debug.Log($"[Diag][Client {OwnerClientId}] syncedTick={_currentTick} serverTick={serverTick} sentInput={_lastSentInputTick} lastServerState={_lastServerStateTick} hasNewState={_hasNewServerState} pos={_rb.position} speed={speed:0.00} throttle={_inputManager?.throttleInput:0.00} steer={_inputManager?.steerInput:0.00} connected={NetworkManager.Singleton.IsConnectedClient} focused={Application.isFocused} kinematic={_rb.isKinematic} fixedDt={Time.fixedDeltaTime:0.0000}");
        }

        if (IsServer && !IsOwner)
        {
            Debug.Log($"[Diag][Server view of client {OwnerClientId}] syncedTick={_currentTick} lastInputTick={_serverLastReceivedTick} lastAppliedTick={_lastAppliedInput.Tick} lastInputAge={(Time.time - _lastInputReceivedTime):0.00}s");
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
    }
}
