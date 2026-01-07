using System.Collections.Concurrent;

namespace JitRealm.Mud;

/// <summary>
/// Thread-safe message queue for storing messages until displayed.
/// </summary>
public sealed class MessageQueue
{
    private readonly ConcurrentQueue<MudMessage> _messages = new();

    /// <summary>
    /// Optional callback for immediate message delivery.
    /// Returns true if message was delivered (skip queue), false to queue for later.
    /// Used for async scenarios (e.g., LLM responses) where messages need immediate delivery.
    /// </summary>
    public Func<MudMessage, bool>? ImmediateDeliveryHandler { get; set; }

    /// <summary>
    /// Enqueue a message for later delivery.
    /// If ImmediateDeliveryHandler returns true, the message is NOT queued (already delivered).
    /// </summary>
    public void Enqueue(MudMessage message)
    {
        // Try immediate delivery first
        if (ImmediateDeliveryHandler?.Invoke(message) == true)
        {
            // Message was delivered immediately, don't queue
            return;
        }

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
