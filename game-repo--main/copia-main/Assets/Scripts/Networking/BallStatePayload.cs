using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Authoritative ball state broadcast from server to all clients at 30Hz.
/// Clients interpolate between received states for smooth rendering.
/// </summary>
public struct BallStatePayload : INetworkSerializable
{
    public int Tick;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref Rotation);
        serializer.SerializeValue(ref Velocity);
        serializer.SerializeValue(ref AngularVelocity);
    }
}
