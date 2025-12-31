using System.Diagnostics;

namespace JitRealm.Mud.Diagnostics;

/// <summary>
/// Low-overhead timings/counters for the main game loop.
/// Designed for "last tick" visibility and coarse aggregation without allocations.
/// </summary>
public sealed class LoopMetrics
{
    // Last-tick durations (Stopwatch ticks)
    private long _lastHeartbeatsTicks;
    private long _lastCalloutsTicks;
    private long _lastCombatTicks;
    private long _lastInputTicks;
    private long _lastDeliverTicks;
    private long _lastTickTicks;

    // Last-tick counts
    private int _lastDueHeartbeats;
    private int _lastDueCallouts;

    // Aggregation
    private long _tickCount;

    public static double TicksToMs(long swTicks) =>
        swTicks <= 0 ? 0 : (swTicks * 1000.0) / Stopwatch.Frequency;

    public void RecordTick(
        long heartbeatsTicks,
        int dueHeartbeats,
        long calloutsTicks,
        int dueCallouts,
        long combatTicks,
        long inputTicks,
        long deliverTicks,
        long totalTicks)
    {
        _lastHeartbeatsTicks = heartbeatsTicks;
        _lastCalloutsTicks = calloutsTicks;
        _lastCombatTicks = combatTicks;
        _lastInputTicks = inputTicks;
        _lastDeliverTicks = deliverTicks;
        _lastTickTicks = totalTicks;
        _lastDueHeartbeats = dueHeartbeats;
        _lastDueCallouts = dueCallouts;
        _tickCount++;
    }

    public long TickCount => Interlocked.Read(ref _tickCount);

    public (double ms, int due) HeartbeatsLast => (TicksToMs(_lastHeartbeatsTicks), _lastDueHeartbeats);
    public (double ms, int due) CalloutsLast => (TicksToMs(_lastCalloutsTicks), _lastDueCallouts);
    public double CombatLastMs => TicksToMs(_lastCombatTicks);
    public double InputLastMs => TicksToMs(_lastInputTicks);
    public double DeliverLastMs => TicksToMs(_lastDeliverTicks);
    public double TickLastMs => TicksToMs(_lastTickTicks);
}


