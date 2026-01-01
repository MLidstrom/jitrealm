using System.Collections.Concurrent;

namespace JitRealm.Mud;

/// <summary>
/// Stores recent events per room for NPC environmental awareness.
/// Events are automatically pruned after a configurable time window.
/// </summary>
public sealed class RoomEventLog
{
    private readonly ConcurrentDictionary<string, List<TimestampedEvent>> _events = new();
    private readonly TimeSpan _eventLifetime = TimeSpan.FromMinutes(5);
    private readonly int _maxEventsPerRoom = 20;

    /// <summary>
    /// Record an event in a room.
    /// </summary>
    /// <param name="roomId">The room where the event occurred.</param>
    /// <param name="description">Description of the event.</param>
    public void Record(string roomId, string description)
    {
        var events = _events.GetOrAdd(roomId, _ => new List<TimestampedEvent>());
        lock (events)
        {
            events.Add(new TimestampedEvent(DateTime.UtcNow, description));

            // Prune old events
            var cutoff = DateTime.UtcNow - _eventLifetime;
            events.RemoveAll(e => e.Timestamp < cutoff);

            // Limit total events
            if (events.Count > _maxEventsPerRoom)
            {
                events.RemoveRange(0, events.Count - _maxEventsPerRoom);
            }
        }
    }

    /// <summary>
    /// Get recent events for a room.
    /// </summary>
    /// <param name="roomId">The room to get events for.</param>
    /// <param name="maxCount">Maximum number of events to return.</param>
    /// <returns>List of event descriptions, most recent last.</returns>
    public IReadOnlyList<string> GetEvents(string roomId, int maxCount = 10)
    {
        if (!_events.TryGetValue(roomId, out var events))
        {
            return Array.Empty<string>();
        }

        lock (events)
        {
            // Prune old events
            var cutoff = DateTime.UtcNow - _eventLifetime;
            events.RemoveAll(e => e.Timestamp < cutoff);

            return events
                .TakeLast(maxCount)
                .Select(e => e.Description)
                .ToList();
        }
    }

    /// <summary>
    /// Clear all events for a room.
    /// </summary>
    public void ClearRoom(string roomId)
    {
        _events.TryRemove(roomId, out _);
    }

    private sealed record TimestampedEvent(DateTime Timestamp, string Description);
}
