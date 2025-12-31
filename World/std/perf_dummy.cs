using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Minimal object for driver performance testing.
/// - Implements IHeartbeat with a no-op heartbeat.
/// - Exposes a no-op callout method named "Tick" to exercise callout dispatch.
/// </summary>
public sealed class PerfDummy : MudObjectBase, IHeartbeat
{
    public override string Name => "perf_dummy";

    public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(1);

    public override IReadOnlyDictionary<string, string> Details => new Dictionary<string, string>();

    public void Heartbeat(IMudContext ctx)
    {
        // Intentionally no-op. This exists to measure scheduler/dispatch overhead.
    }

    public void Tick(IMudContext ctx)
    {
        // Intentionally no-op. This exists to measure callout dispatch overhead.
    }
}


