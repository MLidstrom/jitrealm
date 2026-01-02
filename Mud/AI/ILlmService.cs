namespace JitRealm.Mud.AI;

/// <summary>
/// Service interface for LLM completions.
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Whether the LLM service is enabled and available.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Generate a completion from the LLM.
    /// </summary>
    /// <param name="systemPrompt">The system prompt defining behavior.</param>
    /// <param name="userMessage">The user/player message to respond to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The LLM's response text, or null if disabled/failed.</returns>
    Task<string?> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a completion with conversation history.
    /// </summary>
    /// <param name="systemPrompt">The system prompt defining behavior.</param>
    /// <param name="messages">List of (role, content) tuples for conversation history.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The LLM's response text, or null if disabled/failed.</returns>
    Task<string?> CompleteWithHistoryAsync(
        string systemPrompt,
        IReadOnlyList<(string role, string content)> messages,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context information for NPC decision-making.
/// Provides a complete view of the NPC's environment.
/// </summary>
public sealed class NpcContext
{
    // NPC's own state
    public required string NpcId { get; init; }
    public required string NpcName { get; init; }
    public required int CurrentHP { get; init; }
    public required int MaxHP { get; init; }
    public required bool InCombat { get; init; }
    public required string? CombatTargetId { get; init; }
    public required string? CombatTargetName { get; init; }

    // Species/type capabilities
    public NpcCapabilities Capabilities { get; init; } = NpcCapabilities.Humanoid;

    // Room information
    public required string RoomId { get; init; }
    public required string RoomName { get; init; }
    public required string RoomDescription { get; init; }
    public required IReadOnlyList<string> RoomExits { get; init; }

    // Entities in the room
    public required IReadOnlyList<EntityInfo> PlayersInRoom { get; init; }
    public required IReadOnlyList<EntityInfo> NpcsInRoom { get; init; }
    public required IReadOnlyList<string> ItemsInRoom { get; init; }

    // Recent events the NPC witnessed
    public required IReadOnlyList<string> RecentEvents { get; init; }

    // Shop inventory (for merchants implementing IShopkeeper)
    public IReadOnlyList<ShopItem>? ShopStock { get; init; }

    /// <summary>
    /// Check if this NPC has a specific capability.
    /// </summary>
    public bool Can(NpcCapabilities capability) => (Capabilities & capability) != 0;

    /// <summary>
    /// Build a text description of what the NPC sees, suitable for LLM context.
    /// </summary>
    public string BuildEnvironmentDescription()
    {
        var lines = new List<string>();

        // NPC's own status
        var hpPercent = MaxHP > 0 ? (CurrentHP * 100 / MaxHP) : 100;
        var healthDesc = hpPercent switch
        {
            <= 10 => "near death",
            <= 25 => "badly wounded",
            <= 50 => "wounded",
            <= 75 => "slightly hurt",
            _ => "healthy"
        };
        lines.Add($"[Your status: {healthDesc} ({CurrentHP}/{MaxHP} HP)]");

        if (InCombat && CombatTargetName is not null)
        {
            lines.Add($"[You are in combat with {CombatTargetName}!]");
        }

        // Room description
        lines.Add($"\n[Location: {RoomName}]");
        lines.Add(RoomDescription);

        if (RoomExits.Count > 0)
        {
            lines.Add($"Exits: {string.Join(", ", RoomExits)}");
        }

        // Who's here
        if (PlayersInRoom.Count > 0)
        {
            var playerDescs = PlayersInRoom.Select(p =>
                p.InCombat ? $"{p.Name} (fighting)" : p.Name);
            lines.Add($"\nPlayers here: {string.Join(", ", playerDescs)}");
        }

        if (NpcsInRoom.Count > 0)
        {
            var npcDescs = NpcsInRoom.Select(n =>
                n.InCombat ? $"{n.Name} (fighting)" : n.Name);
            lines.Add($"Others here: {string.Join(", ", npcDescs)}");
        }

        if (ItemsInRoom.Count > 0)
        {
            lines.Add($"Items: {string.Join(", ", ItemsInRoom)}");
        }

        // Recent events
        if (RecentEvents.Count > 0)
        {
            lines.Add($"\n[Recent events:]");
            foreach (var evt in RecentEvents.TakeLast(5))
            {
                lines.Add($"- {evt}");
            }
        }

        // Shop inventory (for merchants)
        if (ShopStock is { Count: > 0 })
        {
            lines.Add($"\n[Your shop inventory - items for sale:]");
            foreach (var item in ShopStock)
            {
                var stockInfo = item.Stock < 0 ? "" : $" ({item.Stock} in stock)";
                var desc = string.IsNullOrEmpty(item.Description) ? "" : $" - {item.Description}";
                lines.Add($"- {item.Name}: {item.Price} gold{stockInfo}{desc}");
            }
            lines.Add("\nWhen asked about stock, list these items with their prices!");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build instructions for LLM about what actions this NPC can take.
    /// Based on species capabilities.
    /// </summary>
    public string BuildActionInstructions()
    {
        var actions = new List<string>();

        if (Can(NpcCapabilities.CanSpeak))
            actions.Add("SPEAK (say words aloud)");
        else
            actions.Add("You CANNOT speak - communicate only through sounds and body language");

        if (Can(NpcCapabilities.CanEmote))
            actions.Add("EMOTE (physical actions, sounds, expressions)");

        if (Can(NpcCapabilities.CanAttack))
            actions.Add("ATTACK (if threatened)");

        if (Can(NpcCapabilities.CanFlee))
            actions.Add("FLEE (if outmatched)");

        if (Can(NpcCapabilities.CanWander) && RoomExits.Count > 0)
            actions.Add($"MOVE to: {string.Join(", ", RoomExits)}");

        var lines = new List<string> { "[Available actions:]" };
        lines.AddRange(actions.Select(a => $"- {a}"));

        return string.Join("\n", lines);
    }
}

/// <summary>
/// Information about an entity (player or NPC) in a room.
/// </summary>
public sealed class EntityInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool InCombat { get; init; }
    public int? HP { get; init; }
    public int? MaxHP { get; init; }
}

/// <summary>
/// Parsed action from LLM response.
/// </summary>
public sealed class NpcAction
{
    public NpcActionType Type { get; init; }
    public string? Target { get; init; }
    public string? Message { get; init; }

    public static NpcAction Idle => new() { Type = NpcActionType.Idle };
    public static NpcAction Say(string message) => new() { Type = NpcActionType.Say, Message = message };
    public static NpcAction Emote(string message) => new() { Type = NpcActionType.Emote, Message = message };
    public static NpcAction Attack(string target) => new() { Type = NpcActionType.Attack, Target = target };
    public static NpcAction Flee => new() { Type = NpcActionType.Flee };
}

/// <summary>
/// Types of actions an NPC can take.
/// </summary>
public enum NpcActionType
{
    Idle,
    Say,
    Emote,
    Attack,
    Flee,
    Give,
    Trade
}
