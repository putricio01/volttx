using Unity.Netcode;

/// <summary>
/// Batch of recent inputs sent from owner client to server.
/// This adds loss-tolerance when using unreliable RPC delivery by redundantly
/// including a few previous ticks in each send.
/// </summary>
public struct InputBatchPayload : INetworkSerializable
{
    public const int MaxInputs = 8;

    public int Count;
    public int LatestServerStateAckTick;

    public InputPayload Input0;
    public InputPayload Input1;
    public InputPayload Input2;
    public InputPayload Input3;
    public InputPayload Input4;
    public InputPayload Input5;
    public InputPayload Input6;
    public InputPayload Input7;

    public void SetAt(int index, InputPayload payload)
    {
        switch (index)
        {
            case 0: Input0 = payload; break;
            case 1: Input1 = payload; break;
            case 2: Input2 = payload; break;
            case 3: Input3 = payload; break;
            case 4: Input4 = payload; break;
            case 5: Input5 = payload; break;
            case 6: Input6 = payload; break;
            case 7: Input7 = payload; break;
        }
    }

    public InputPayload GetAt(int index)
    {
        switch (index)
        {
            case 0: return Input0;
            case 1: return Input1;
            case 2: return Input2;
            case 3: return Input3;
            case 4: return Input4;
            case 5: return Input5;
            case 6: return Input6;
            case 7: return Input7;
            default: return default;
        }
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Count);
        serializer.SerializeValue(ref LatestServerStateAckTick);
        Count = Count < 0 ? 0 : (Count > MaxInputs ? MaxInputs : Count);

        if (Count > 0) serializer.SerializeValue(ref Input0);
        if (Count > 1) serializer.SerializeValue(ref Input1);
        if (Count > 2) serializer.SerializeValue(ref Input2);
        if (Count > 3) serializer.SerializeValue(ref Input3);
        if (Count > 4) serializer.SerializeValue(ref Input4);
        if (Count > 5) serializer.SerializeValue(ref Input5);
        if (Count > 6) serializer.SerializeValue(ref Input6);
        if (Count > 7) serializer.SerializeValue(ref Input7);
    }
}
