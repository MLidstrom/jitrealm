namespace JitRealm.Mud.Network;

/// <summary>
/// A session implementation for NPCs that don't have a real network connection.
/// Output is collected for potential logging/debugging but not sent anywhere.
/// </summary>
public sealed class NpcSession : ISession
{
    private readonly List<string> _outputBuffer = new();

    public NpcSession(string npcId, string npcName)
    {
        SessionId = $"npc-{npcId}";
        PlayerId = npcId;
        PlayerName = npcName;
    }

    public string SessionId { get; }
    public string? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public bool IsWizard { get; set; } = false;
    public bool IsConnected => true;
    public bool HasPendingInput => false;

    /// <summary>
    /// Get all output that was written to this session.
    /// Useful for debugging NPC command execution.
    /// </summary>
    public IReadOnlyList<string> GetOutput() => _outputBuffer;

    /// <summary>
    /// Clear the output buffer.
    /// </summary>
    public void ClearOutput() => _outputBuffer.Clear();

    public Task WriteLineAsync(string text)
    {
        _outputBuffer.Add(text);
        return Task.CompletedTask;
    }

    public Task WriteAsync(string text)
    {
        // Append to last line if exists, otherwise create new
        if (_outputBuffer.Count > 0)
        {
            _outputBuffer[^1] += text;
        }
        else
        {
            _outputBuffer.Add(text);
        }
        return Task.CompletedTask;
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        // NPCs don't read input from the session
        return Task.FromResult<string?>(null);
    }

    public Task CloseAsync()
    {
        // Nothing to close for NPC sessions
        return Task.CompletedTask;
    }
}
