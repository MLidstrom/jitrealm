namespace JitRealm.Mud;

/// <summary>
/// Scheduler for delayed method invocations (callouts).
/// Uses a priority queue sorted by fire time.
/// </summary>
public sealed class CallOutScheduler
{
    private readonly PriorityQueue<CallOutEntry, DateTimeOffset> _queue = new();
    private readonly Dictionary<long, CallOutEntry> _entriesById = new();
    private readonly IClock _clock;

    public CallOutScheduler(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// Schedule a one-time callout.
    /// </summary>
    public long Schedule(string targetId, string methodName, TimeSpan delay, object?[]? args = null)
    {
        var entry = new CallOutEntry
        {
            TargetId = targetId,
            MethodName = methodName,
            FireTime = _clock.Now + delay,
            Args = args,
            RepeatInterval = null
        };

        _queue.Enqueue(entry, entry.FireTime);
        _entriesById[entry.Id] = entry;
        return entry.Id;
    }

    /// <summary>
    /// Schedule a repeating callout.
    /// </summary>
    public long ScheduleEvery(string targetId, string methodName, TimeSpan interval, object?[]? args = null)
    {
        var entry = new CallOutEntry
        {
            TargetId = targetId,
            MethodName = methodName,
            FireTime = _clock.Now + interval,
            Args = args,
            RepeatInterval = interval
        };

        _queue.Enqueue(entry, entry.FireTime);
        _entriesById[entry.Id] = entry;
        return entry.Id;
    }

    /// <summary>
    /// Cancel a scheduled callout by ID.
    /// </summary>
    public bool Cancel(long calloutId)
    {
        if (_entriesById.TryGetValue(calloutId, out var entry))
        {
            entry.IsCancelled = true;
            _entriesById.Remove(calloutId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Cancel all callouts for a specific target object.
    /// </summary>
    public int CancelAllForTarget(string targetId)
    {
        var toCancel = _entriesById.Values
            .Where(e => e.TargetId.Equals(targetId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in toCancel)
        {
            entry.IsCancelled = true;
            _entriesById.Remove(entry.Id);
        }

        return toCancel.Count;
    }

    /// <summary>
    /// Get all callouts that are due to fire.
    /// Removes one-time callouts, re-schedules repeating ones.
    /// </summary>
    public IReadOnlyList<CallOutEntry> GetDueCallouts()
    {
        var now = _clock.Now;
        var due = new List<CallOutEntry>();

        while (_queue.TryPeek(out var entry, out var fireTime))
        {
            if (fireTime > now)
                break;

            _queue.Dequeue();

            // Skip cancelled entries
            if (entry.IsCancelled)
            {
                _entriesById.Remove(entry.Id);
                continue;
            }

            due.Add(entry);

            // Re-schedule if repeating
            if (entry.RepeatInterval.HasValue)
            {
                var nextEntry = new CallOutEntry
                {
                    TargetId = entry.TargetId,
                    MethodName = entry.MethodName,
                    FireTime = now + entry.RepeatInterval.Value,
                    Args = entry.Args,
                    RepeatInterval = entry.RepeatInterval
                };

                _queue.Enqueue(nextEntry, nextEntry.FireTime);
                _entriesById.Remove(entry.Id);
                _entriesById[nextEntry.Id] = nextEntry;
            }
            else
            {
                _entriesById.Remove(entry.Id);
            }
        }

        return due;
    }

    /// <summary>
    /// Get count of pending callouts.
    /// </summary>
    public int Count => _entriesById.Count;
}
