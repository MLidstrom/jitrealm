using Pgvector;

namespace JitRealm.Mud.AI;

/// <summary>
/// Stateless rules for promoting transient room events into persistent per-NPC memories.
/// Keep this conservative: write amplification kills scale.
/// </summary>
internal static class MemoryPromotionRules
{
    public static NpcMemoryWrite? TryCreateObserverMemory(
        WorldState state,
        string observerNpcId,
        IMudObject observer,
        RoomEvent roomEvent,
        string roomId)
    {
        // Only create memories for NPC observers about players (stable account/player name).
        var actorSession = state.Sessions.GetByPlayerId(roomEvent.ActorId);
        if (actorSession?.PlayerName is null)
            return null;

        // Don't store memories about ourselves.
        if (observerNpcId == roomEvent.ActorId)
            return null;

        // Avoid recording pure ambient chatter by default.
        if (roomEvent.Type == RoomEventType.Speech)
        {
            if (!IsSpeechDirectedAtObserver(state, observer, roomEvent, roomId))
                return null;
        }

        // Decide kind/importance/content.
        string kind;
        int importance;
        string content;

        switch (roomEvent.Type)
        {
            case RoomEventType.ItemGiven:
                kind = "gift_received";
                importance = 70;
                content = $"Received {roomEvent.Message} from {actorSession.PlayerName}";
                break;

            case RoomEventType.Combat:
                kind = "combat";
                importance = 80;
                content = $"Was attacked by {actorSession.PlayerName}";
                break;

            case RoomEventType.Death:
                kind = "witnessed_death";
                importance = 90;
                content = $"Witnessed {actorSession.PlayerName} die";
                break;

            case RoomEventType.Speech:
                kind = "conversation";
                importance = 30;
                content = $"Had a conversation with {actorSession.PlayerName} who said: \"{roomEvent.Message}\"";
                break;

            default:
                return null;
        }

        // Bound content to keep DB + prompts sane.
        content = BoundContent(content, 512);

        return new NpcMemoryWrite(
            Id: Guid.NewGuid(),
            NpcId: observerNpcId,
            SubjectPlayer: actorSession.PlayerName,
            RoomId: roomId,
            AreaId: null,
            Kind: kind,
            Importance: importance,
            Tags: new[] { $"room:{roomId}" },
            Content: content,
            ExpiresAt: kind == "conversation" ? DateTimeOffset.UtcNow.AddDays(7) : null,
            Embedding: null);
    }

    private static bool IsSpeechDirectedAtObserver(WorldState state, IMudObject observer, RoomEvent roomEvent, string roomId)
    {
        // 1:1 conversation: if only the speaker and one NPC are in the room, all speech is directed
        if (IsOneOnOneConversation(state, roomEvent.ActorId, roomId))
            return true;

        // Otherwise, check if the message mentions the NPC
        var message = roomEvent.Message;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var lower = message.ToLowerInvariant();

        // If the observer is a LivingBase, use its aliases (role + proper name).
        if (observer is LivingBase living)
        {
            foreach (var alias in living.Aliases)
            {
                var a = alias?.Trim();
                if (string.IsNullOrWhiteSpace(a)) continue;
                if (lower.Contains(a.ToLowerInvariant()))
                    return true;
            }
        }

        // Fallback: check Name
        return lower.Contains(observer.Name.ToLowerInvariant());
    }

    private static bool IsOneOnOneConversation(WorldState state, string speakerId, string roomId)
    {
        // Get all livings in the room
        var roomContents = state.Containers.GetContents(roomId);
        int livingCount = 0;

        foreach (var id in roomContents)
        {
            var obj = state.Objects?.Get<ILiving>(id);
            if (obj is not null)
                livingCount++;

            // If more than 2 livings (speaker + 1 NPC), it's not 1:1
            if (livingCount > 2)
                return false;
        }

        // 1:1 means exactly 2 livings: the speaker and one other
        return livingCount == 2;
    }

    private static string BoundContent(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        text = text.Trim();
        if (text.Length <= maxChars)
            return text;
        return text.Substring(0, maxChars).TrimEnd() + "...";
    }
}


