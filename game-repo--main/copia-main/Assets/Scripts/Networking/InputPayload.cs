using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tick-stamped input snapshot sent from client to server each physics tick.
/// ~30 bytes per payload. At 60Hz = 1.8 KB/s per player.
/// </summary>
public struct InputPayload : INetworkSerializable
{
    public int Tick;
    public float ThrottleInput;
    public float SteerInput;
    public float YawInput;
    public float PitchInput;
    public float RollInput;
    public bool IsBoost;
    public bool IsDrift;
    public bool IsAirRoll;
    public bool IsJump;
    public bool IsJumpUp;
    public bool IsJumpDown;

    static float SanitizeAxis(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0f;

        return Mathf.Clamp(value, -1f, 1f);
    }

    public void ClampAnalogInputs()
    {
        ThrottleInput = SanitizeAxis(ThrottleInput);
        SteerInput = SanitizeAxis(SteerInput);
        YawInput = SanitizeAxis(YawInput);
        PitchInput = SanitizeAxis(PitchInput);
        RollInput = SanitizeAxis(RollInput);
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref ThrottleInput);
        serializer.SerializeValue(ref SteerInput);
        serializer.SerializeValue(ref YawInput);
        serializer.SerializeValue(ref PitchInput);
        serializer.SerializeValue(ref RollInput);
        serializer.SerializeValue(ref IsBoost);
        serializer.SerializeValue(ref IsDrift);
        serializer.SerializeValue(ref IsAirRoll);
        serializer.SerializeValue(ref IsJump);
        serializer.SerializeValue(ref IsJumpUp);
        serializer.SerializeValue(ref IsJumpDown);
    }
}
