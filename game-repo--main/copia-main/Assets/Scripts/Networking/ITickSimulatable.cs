/// <summary>
/// Components that affect deterministic-ish car simulation should expose their
/// per-tick logic through this interface so rollback can replay the same logic
/// that normally runs from FixedUpdate().
/// </summary>
public interface ITickSimulatable
{
    int TickOrder { get; }
    void SimulateNetworkTick();
}

/// <summary>
/// Explicit ordering for car sub-systems during tick replay.
/// </summary>
public static class TickSimulationOrder
{
    public const int SurfaceContacts = 100;
    public const int CarState = 200;
    public const int GroundControl = 300;
    public const int Wheels = 400;
    public const int Jumping = 500;
    public const int AirControl = 600;
    public const int Boosting = 700;
}
