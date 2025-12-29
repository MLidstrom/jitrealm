namespace JitRealm.Mud;

/// <summary>
/// Entry tracking the next heartbeat time for an object.
/// </summary>
public sealed class HeartbeatEntry
{
    public required string ObjectId { get; init; }
    public required TimeSpan Interval { get; init; }
    public DateTimeOffset NextFireTime { get; set; }
}

/// <summary>
/// Simple scheduler for IHeartbeat objects.
/// Tracks next fire time per object and returns due heartbeats.
/// </summary>
public sealed class HeartbeatScheduler
{
    private readonly Dictionary<string, HeartbeatEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
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
        _entries[objectId] = new HeartbeatEntry
        {
            ObjectId = objectId,
            Interval = interval,
            NextFireTime = _clock.Now + interval
        };
    }

    /// <summary>
    /// Unregister an object from heartbeat scheduling.
    /// </summary>
    public void Unregister(string objectId)
    {
        _entries.Remove(objectId);
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
        var now = _clock.Now;
        var due = new List<string>();

        foreach (var entry in _entries.Values)
        {
            if (now >= entry.NextFireTime)
            {
                due.Add(entry.ObjectId);
                entry.NextFireTime = now + entry.Interval;
            }
        }

        return due;
    }

    /// <summary>
    /// Get count of registered heartbeat objects.
    /// </summary>
    public int Count => _entries.Count;
}
