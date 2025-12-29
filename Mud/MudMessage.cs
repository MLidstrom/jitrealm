namespace JitRealm.Mud;

/// <summary>
/// Type of message for display formatting.
/// </summary>
public enum MessageType
{
    Tell,   // Private message: "X tells you: ..."
    Say,    // Room message: "X says: ..."
    Emote   // Action: "X smiles."
}

/// <summary>
/// A message queued for delivery to a player or room.
/// </summary>
public sealed record MudMessage(
    string FromId,
    string? ToId,     // null = broadcast to room
    MessageType Type,
    string Content,
    string? RoomId    // For Say/Emote, the room where it occurred
);
