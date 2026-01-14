namespace JitRealm.Mud.AI;

using JitRealm.Mud.Network;

/// <summary>
/// Manages NPC trace subscriptions for wizard debugging.
/// Wizards can subscribe to trace events from specific NPCs.
/// </summary>
public sealed class NpcTraceManager
{
    private readonly Dictionary<string, HashSet<string>> _npcToSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _sessionToNpcs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// Start tracing an NPC for a session.
    /// </summary>
    public void StartTrace(string sessionId, string npcId)
    {
        lock (_lock)
        {
            if (!_npcToSessions.TryGetValue(npcId, out var sessions))
            {
                sessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _npcToSessions[npcId] = sessions;
            }
            sessions.Add(sessionId);

            if (!_sessionToNpcs.TryGetValue(sessionId, out var npcs))
            {
                npcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _sessionToNpcs[sessionId] = npcs;
            }
            npcs.Add(npcId);
        }
    }

    /// <summary>
    /// Stop tracing an NPC for a session.
    /// </summary>
    public void StopTrace(string sessionId, string npcId)
    {
        lock (_lock)
        {
            if (_npcToSessions.TryGetValue(npcId, out var sessions))
            {
                sessions.Remove(sessionId);
                if (sessions.Count == 0)
                    _npcToSessions.Remove(npcId);
            }

            if (_sessionToNpcs.TryGetValue(sessionId, out var npcs))
            {
                npcs.Remove(npcId);
                if (npcs.Count == 0)
                    _sessionToNpcs.Remove(sessionId);
            }
        }
    }

    /// <summary>
    /// Stop all traces for a session.
    /// </summary>
    public void StopAllTraces(string sessionId)
    {
        lock (_lock)
        {
            if (_sessionToNpcs.TryGetValue(sessionId, out var npcs))
            {
                foreach (var npcId in npcs.ToList())
                {
                    if (_npcToSessions.TryGetValue(npcId, out var sessions))
                    {
                        sessions.Remove(sessionId);
                        if (sessions.Count == 0)
                            _npcToSessions.Remove(npcId);
                    }
                }
                _sessionToNpcs.Remove(sessionId);
            }
        }
    }

    /// <summary>
    /// Check if an NPC is being traced by any session.
    /// </summary>
    public bool IsTraced(string npcId)
    {
        lock (_lock)
        {
            return _npcToSessions.ContainsKey(npcId);
        }
    }

    /// <summary>
    /// Get all session IDs tracing a specific NPC.
    /// </summary>
    public IReadOnlyList<string> GetTracingSessions(string npcId)
    {
        lock (_lock)
        {
            if (_npcToSessions.TryGetValue(npcId, out var sessions))
                return sessions.ToList();
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Get all NPCs being traced by a session.
    /// </summary>
    public IReadOnlyList<string> GetTracedNpcs(string sessionId)
    {
        lock (_lock)
        {
            if (_sessionToNpcs.TryGetValue(sessionId, out var npcs))
                return npcs.ToList();
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Emit a trace event to all sessions tracing the specified NPC.
    /// </summary>
    public async Task EmitAsync(string npcId, string category, string message, SessionManager sessions)
    {
        var sessionIds = GetTracingSessions(npcId);
        if (sessionIds.Count == 0)
            return;

        // Extract short NPC name from ID (e.g., "npcs/villager_tom.cs#000001" -> "villager_tom")
        var shortName = ExtractShortName(npcId);
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[TRACE {timestamp}] [{shortName}] [{category}] {message}";

        foreach (var sessionId in sessionIds)
        {
            var session = sessions.GetBySessionId(sessionId);
            if (session?.IsConnected == true)
            {
                try
                {
                    await session.WriteLineAsync(session.Formatter.FormatInfo(line));
                }
                catch
                {
                    // Session may have disconnected
                }
            }
        }
    }

    /// <summary>
    /// Emit trace event synchronously (fire-and-forget).
    /// </summary>
    public void Emit(string npcId, string category, string message, SessionManager sessions)
    {
        _ = EmitAsync(npcId, category, message, sessions);
    }

    private static string ExtractShortName(string npcId)
    {
        // "npcs/villager_tom.cs#000001" -> "villager_tom"
        var name = npcId;
        var slashIdx = name.LastIndexOf('/');
        if (slashIdx >= 0)
            name = name.Substring(slashIdx + 1);
        var dotIdx = name.IndexOf('.');
        if (dotIdx >= 0)
            name = name.Substring(0, dotIdx);
        return name;
    }
}

/// <summary>
/// Trace event categories.
/// </summary>
public static class TraceCategory
{
    public const string Goal = "GOAL";
    public const string Plan = "PLAN";
    public const string Step = "STEP";
    public const string Path = "PATH";
    public const string Cmd = "CMD";
    public const string Llm = "LLM";
    public const string Memory = "MEM";
    public const string Event = "EVENT";
}
