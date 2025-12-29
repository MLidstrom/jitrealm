namespace JitRealm.Mud;

/// <summary>
/// Implementation of IMudContext, providing world code access to driver services.
/// </summary>
public sealed class MudContext : IMudContext
{
    public required WorldState World { get; init; }
    public required IStateStore State { get; init; }
    public required IClock Clock { get; init; }

    /// <summary>
    /// The ID of the current object this context is associated with.
    /// </summary>
    public string? CurrentObjectId { get; init; }

    /// <summary>
    /// The room ID where this context's object is located (for Say/Emote).
    /// For rooms, this is typically the room's own ID.
    /// </summary>
    public string? RoomId { get; init; }

    public void Tell(string targetId, string message)
    {
        var fromId = CurrentObjectId ?? "unknown";
        World.Messages.Enqueue(new MudMessage(fromId, targetId, MessageType.Tell, message, null));
    }

    public void Say(string message)
    {
        var fromId = CurrentObjectId ?? "unknown";
        var roomId = RoomId ?? World.Player?.LocationId;
        World.Messages.Enqueue(new MudMessage(fromId, null, MessageType.Say, message, roomId));
    }

    public void Emote(string action)
    {
        var fromId = CurrentObjectId ?? "unknown";
        var roomId = RoomId ?? World.Player?.LocationId;
        World.Messages.Enqueue(new MudMessage(fromId, null, MessageType.Emote, action, roomId));
    }

    public long CallOut(string methodName, TimeSpan delay, params object?[] args)
    {
        var targetId = CurrentObjectId ?? throw new InvalidOperationException("No current object for CallOut");
        return World.CallOuts.Schedule(targetId, methodName, delay, args.Length > 0 ? args : null);
    }

    public long Every(string methodName, TimeSpan interval, params object?[] args)
    {
        var targetId = CurrentObjectId ?? throw new InvalidOperationException("No current object for Every");
        return World.CallOuts.ScheduleEvery(targetId, methodName, interval, args.Length > 0 ? args : null);
    }

    public bool CancelCallOut(long calloutId)
    {
        return World.CallOuts.Cancel(calloutId);
    }
}
