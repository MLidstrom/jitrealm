namespace JitRealm.Mud.Network;

/// <summary>
/// Manages active sessions and provides session lookup.
/// </summary>
public sealed class SessionManager
{
    private readonly Dictionary<string, ISession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public event Action<ISession>? OnSessionAdded;
    public event Action<ISession>? OnSessionRemoved;

    public void Add(ISession session)
    {
        lock (_lock)
        {
            _sessions[session.SessionId] = session;
        }
        OnSessionAdded?.Invoke(session);
    }

    public void Remove(string sessionId)
    {
        ISession? session;
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out session))
            {
                _sessions.Remove(sessionId);
            }
        }
        if (session is not null)
        {
            OnSessionRemoved?.Invoke(session);
        }
    }

    public ISession? GetBySessionId(string sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId, out var s) ? s : null;
        }
    }

    /// <summary>
    /// Get session by player ID.
    /// </summary>
    public ISession? GetByPlayerId(string playerId)
    {
        lock (_lock)
        {
            return _sessions.Values.FirstOrDefault(s =>
                s.Player is not null &&
                string.Equals(s.Player.Name, playerId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Get all sessions in a specific room.
    /// </summary>
    public IReadOnlyList<ISession> GetSessionsInRoom(string roomId)
    {
        lock (_lock)
        {
            return _sessions.Values
                .Where(s => s.Player?.LocationId == roomId)
                .ToList();
        }
    }

    /// <summary>
    /// Get all connected sessions.
    /// </summary>
    public IReadOnlyList<ISession> GetAll()
    {
        lock (_lock)
        {
            return _sessions.Values.ToList();
        }
    }

    /// <summary>
    /// Get count of active sessions.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Count;
            }
        }
    }

    /// <summary>
    /// Remove disconnected sessions.
    /// </summary>
    public void PruneDisconnected()
    {
        List<string> toRemove;
        lock (_lock)
        {
            toRemove = _sessions
                .Where(kv => !kv.Value.IsConnected)
                .Select(kv => kv.Key)
                .ToList();
        }

        foreach (var id in toRemove)
        {
            Remove(id);
        }
    }
}
