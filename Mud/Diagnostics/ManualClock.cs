namespace JitRealm.Mud.Diagnostics;

/// <summary>
/// Deterministic clock for benchmarks/tests. Caller controls time progression.
/// </summary>
public sealed class ManualClock : IClock
{
    public ManualClock(DateTimeOffset? start = null)
    {
        Now = start ?? DateTimeOffset.UnixEpoch;
    }

    public DateTimeOffset Now { get; private set; }

    public void Advance(TimeSpan delta)
    {
        Now = Now + delta;
    }
}


