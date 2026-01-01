namespace JitRealm.Mud.AI;

/// <summary>
/// Capabilities that define what actions an NPC can perform.
/// Species-based: a cat can't speak, but can emote (hiss, purr).
/// </summary>
[Flags]
public enum NpcCapabilities
{
    None = 0,

    /// <summary>Can speak intelligible language (say command).</summary>
    CanSpeak = 1 << 0,

    /// <summary>Can perform emotes/actions (emote command).</summary>
    CanEmote = 1 << 1,

    /// <summary>Can initiate combat.</summary>
    CanAttack = 1 << 2,

    /// <summary>Can flee from combat.</summary>
    CanFlee = 1 << 3,

    /// <summary>Can pick up and give items.</summary>
    CanManipulateItems = 1 << 4,

    /// <summary>Can trade with players.</summary>
    CanTrade = 1 << 5,

    /// <summary>Can follow players or other NPCs.</summary>
    CanFollow = 1 << 6,

    /// <summary>Can move between rooms autonomously.</summary>
    CanWander = 1 << 7,

    /// <summary>Can use doors/containers.</summary>
    CanUseDoors = 1 << 8,

    // Common presets
    /// <summary>Basic animal: emote, attack, flee, wander.</summary>
    Animal = CanEmote | CanAttack | CanFlee | CanWander,

    /// <summary>Intelligent humanoid: full capabilities.</summary>
    Humanoid = CanSpeak | CanEmote | CanAttack | CanFlee | CanManipulateItems | CanTrade | CanFollow | CanWander | CanUseDoors,

    /// <summary>Beast/monster: attack, emote, flee.</summary>
    Beast = CanEmote | CanAttack | CanFlee,

    /// <summary>Merchant NPC: speak, trade, no combat.</summary>
    Merchant = CanSpeak | CanEmote | CanManipulateItems | CanTrade,
}

/// <summary>
/// Interface for NPCs that use LLM for conversation and decision-making.
/// NPCs observe room events just like players do and can react to anything.
/// </summary>
public interface ILlmNpc
{
    /// <summary>
    /// The system prompt that defines this NPC's personality and behavior.
    /// Can be loaded from a file or defined inline.
    /// </summary>
    string SystemPrompt { get; }

    /// <summary>
    /// What this NPC can do, based on species/type.
    /// Defaults to Humanoid if not specified.
    /// </summary>
    NpcCapabilities Capabilities => NpcCapabilities.Humanoid;

    /// <summary>
    /// Called when something happens in the room that the NPC observes.
    /// This includes speech, emotes, arrivals, departures, combat, items, etc.
    /// The NPC decides whether and how to react based on its personality.
    /// </summary>
    /// <param name="event">The room event that occurred.</param>
    /// <param name="ctx">The MUD context.</param>
    Task OnRoomEventAsync(RoomEvent @event, IMudContext ctx);
}

/// <summary>
/// Types of events that can occur in a room.
/// </summary>
public enum RoomEventType
{
    /// <summary>Someone spoke aloud.</summary>
    Speech,
    /// <summary>Someone performed an emote/action.</summary>
    Emote,
    /// <summary>Someone arrived in the room.</summary>
    Arrival,
    /// <summary>Someone left the room.</summary>
    Departure,
    /// <summary>Combat action occurred.</summary>
    Combat,
    /// <summary>Item was picked up.</summary>
    ItemTaken,
    /// <summary>Item was dropped.</summary>
    ItemDropped,
    /// <summary>Someone died.</summary>
    Death,
    /// <summary>Other/miscellaneous event.</summary>
    Other
}

/// <summary>
/// Represents an event that occurred in a room, observable by NPCs.
/// </summary>
public sealed class RoomEvent
{
    /// <summary>Type of event that occurred.</summary>
    public required RoomEventType Type { get; init; }

    /// <summary>ID of the entity that caused/performed the event.</summary>
    public required string ActorId { get; init; }

    /// <summary>Display name of the actor.</summary>
    public required string ActorName { get; init; }

    /// <summary>The message/action text (for speech, emotes, etc).</summary>
    public string? Message { get; init; }

    /// <summary>Target of the action, if any (e.g., combat target, item name).</summary>
    public string? Target { get; init; }

    /// <summary>Direction of movement for arrivals/departures.</summary>
    public string? Direction { get; init; }

    /// <summary>Human-readable description of what happened.</summary>
    public string Description => Type switch
    {
        RoomEventType.Speech => $"{ActorName} says: \"{Message}\"",
        RoomEventType.Emote => $"{ActorName} {Message}",
        RoomEventType.Arrival => Direction is not null
            ? $"{ActorName} arrives from the {Direction}."
            : $"{ActorName} has arrived.",
        RoomEventType.Departure => Direction is not null
            ? $"{ActorName} leaves {Direction}."
            : $"{ActorName} has left.",
        RoomEventType.Combat => $"{ActorName} attacks {Target}!",
        RoomEventType.ItemTaken => $"{ActorName} picks up {Target}.",
        RoomEventType.ItemDropped => $"{ActorName} drops {Target}.",
        RoomEventType.Death => $"{ActorName} has died!",
        _ => Message ?? $"{ActorName} does something."
    };
}

/// <summary>
/// Interface for NPCs that can decide actions using LLM.
/// </summary>
public interface ILlmActionNpc : ILlmNpc
{
    /// <summary>
    /// Build context information for LLM decision-making.
    /// </summary>
    NpcContext BuildContext(IMudContext ctx);

    /// <summary>
    /// Execute a parsed action.
    /// </summary>
    void ExecuteAction(NpcAction action, IMudContext ctx);
}
