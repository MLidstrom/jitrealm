namespace JitRealm.Mud;

/// <summary>
/// Simple scheduler for IHeartbeat objects.
/// Tracks next fire time per object and returns due heartbeats.
/// </summary>
public sealed class HeartbeatScheduler
{
    // Keep canonical schedule state per object.
    // Optimization: track the earliest next-fire time so we can avoid scanning unless something is due.
    private readonly Dictionary<string, (long IntervalTicks, long NextFireTicksUtc)> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private long _nextGlobalFireTicksUtc = long.MaxValue;
    private readonly IClock _clock;

    public HeartbeatScheduler(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// Register an object for heartbeat scheduling.
    /// </summary>
    public void Register(string objectId, TimeSpan interval)
    {
        var nowTicks = _clock.Now.UtcTicks;
        var intervalTicks = interval.Ticks;
        var nextTicks = nowTicks + intervalTicks;
        _entries[objectId] = (intervalTicks, nextTicks);

        if (nextTicks < _nextGlobalFireTicksUtc)
            _nextGlobalFireTicksUtc = nextTicks;
    }

    /// <summary>
    /// Unregister an object from heartbeat scheduling.
    /// </summary>
    public void Unregister(string objectId)
    {
        if (_entries.TryGetValue(objectId, out var existing))
        {
            _entries.Remove(objectId);

            // If we removed the entry that was the global minimum, recompute (rare).
            if (existing.NextFireTicksUtc == _nextGlobalFireTicksUtc)
            {
                _nextGlobalFireTicksUtc = ComputeNextGlobalFireTicksUtc();
            }
        }
    }

    /// <summary>
    /// Check if an object is registered.
    /// </summary>
    public bool IsRegistered(string objectId) => _entries.ContainsKey(objectId);

    /// <summary>
    /// Get all object IDs that are due for a heartbeat.
    /// Updates their next fire time automatically.
    /// </summary>
    public IReadOnlyList<string> GetDueHeartbeats()
    {
        var nowTicks = _clock.Now.UtcTicks;
        if (nowTicks < _nextGlobalFireTicksUtc || _entries.Count == 0)
            return Array.Empty<string>();

        // Something is due; scan once, update due entries, and recompute new global minimum.
        var due = new List<string>();
        long newMin = long.MaxValue;

        // Copy to avoid modifying the dictionary while enumerating.
        foreach (var kvp in _entries.ToArray())
        {
            var objectId = kvp.Key;
            var intervalTicks = kvp.Value.IntervalTicks;
            var nextFire = kvp.Value.NextFireTicksUtc;

            if (nowTicks >= nextFire)
            {
                due.Add(objectId);
                nextFire = nowTicks + intervalTicks;
                _entries[objectId] = (intervalTicks, nextFire);
            }

            if (nextFire < newMin)
                newMin = nextFire;
        }

        _nextGlobalFireTicksUtc = newMin;
        return due;
    }

    /// <summary>
    /// Get count of registered heartbeat objects.
    /// </summary>
    public int Count => _entries.Count;

    private long ComputeNextGlobalFireTicksUtc()
    {
        long min = long.MaxValue;
        foreach (var entry in _entries.Values)
        {
            if (entry.NextFireTicksUtc < min)
                min = entry.NextFireTicksUtc;
        }
        return min;
    }
}
