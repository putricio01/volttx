using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Authoritative car state snapshot sent from server to owning client.
/// Used for server reconciliation. ~100 bytes per payload. Server sends at 30Hz = 3 KB/s per player.
/// </summary>
public struct StatePayload : INetworkSerializable
{
    public int Tick;

    // Rigidbody state
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;

    // Car state (from CubeController)
    public bool IsCanDrive;
    public bool IsAllWheelsSurface;
    public int NumWheelsSurface;
    public bool IsBodySurface;
    public float ForwardSpeed;
    public float ForwardSpeedSign;
    public float ForwardSpeedAbs;
    public int CarState; // CubeController.CarStates enum cast to int

    // Jump state (from CubeJumping) — critical for correct reconciliation
    public bool IsJumping;
    public bool IsCanFirstJump;
    public bool IsCanKeepJumping;
    public float JumpTimer;

    // Drift state (from CubeGroundControl)
    public float CurrentWheelSideFriction;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);

        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref Rotation);
        serializer.SerializeValue(ref Velocity);
        serializer.SerializeValue(ref AngularVelocity);

        serializer.SerializeValue(ref IsCanDrive);
        serializer.SerializeValue(ref IsAllWheelsSurface);
        serializer.SerializeValue(ref NumWheelsSurface);
        serializer.SerializeValue(ref IsBodySurface);
        serializer.SerializeValue(ref ForwardSpeed);
        serializer.SerializeValue(ref ForwardSpeedSign);
        serializer.SerializeValue(ref ForwardSpeedAbs);
        serializer.SerializeValue(ref CarState);

        serializer.SerializeValue(ref IsJumping);
        serializer.SerializeValue(ref IsCanFirstJump);
        serializer.SerializeValue(ref IsCanKeepJumping);
        serializer.SerializeValue(ref JumpTimer);

        serializer.SerializeValue(ref CurrentWheelSideFriction);
    }
}
