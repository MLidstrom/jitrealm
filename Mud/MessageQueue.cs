using System.Collections.Concurrent;

namespace JitRealm.Mud;

/// <summary>
/// Thread-safe message queue for storing messages until displayed.
/// </summary>
public sealed class MessageQueue
{
    private readonly ConcurrentQueue<MudMessage> _messages = new();

    /// <summary>
    /// Enqueue a message for later delivery.
    /// </summary>
    public void Enqueue(MudMessage message)
    {
        _messages.Enqueue(message);
    }

    /// <summary>
    /// Drain all pending messages from the queue.
    /// </summary>
    public IReadOnlyList<MudMessage> Drain()
    {
        var result = new List<MudMessage>();
        while (_messages.TryDequeue(out var msg))
        {
            result.Add(msg);
        }
        return result;
    }

    /// <summary>
    /// Check if there are any pending messages.
    /// </summary>
    public bool HasMessages => !_messages.IsEmpty;
}
