using JitRealm.Mud.AI;
using JitRealm.Mud.Network;

namespace JitRealm.Mud.Commands;

/// <summary>
/// Context passed to commands providing access to world state and player information.
/// </summary>
public class CommandContext
{
    /// <summary>
    /// The world state containing all game data.
    /// </summary>
    public required WorldState State { get; init; }

    /// <summary>
    /// The player's object ID.
    /// </summary>
    public required string PlayerId { get; init; }

    /// <summary>
    /// The session for this player.
    /// </summary>
    public required ISession Session { get; init; }

    /// <summary>
    /// Action to output text to the player.
    /// </summary>
    public required Action<string> Output { get; init; }

    /// <summary>
    /// Function to create an IMudContext for world objects.
    /// </summary>
    public required Func<string, MudContext> CreateContext { get; init; }

    /// <summary>
    /// The raw input string that triggered this command.
    /// </summary>
    public string RawInput { get; init; } = "";

    /// <summary>
    /// Get the player's current location (room ID).
    /// </summary>
    public string? GetPlayerLocation()
    {
        return State.Containers.GetContainer(PlayerId);
    }

    /// <summary>
    /// Get the player object.
    /// </summary>
    public IPlayer? GetPlayer()
    {
        return State.Objects?.Get<IPlayer>(PlayerId);
    }

    /// <summary>
    /// Get the current room.
    /// </summary>
    public IRoom? GetCurrentRoom()
    {
        var roomId = GetPlayerLocation();
        return roomId is not null ? State.Objects?.Get<IRoom>(roomId) : null;
    }

    /// <summary>
    /// Whether the current player is a wizard.
    /// </summary>
    public bool IsWizard => Session.IsWizard;

    /// <summary>
    /// Output a formatted error message.
    /// </summary>
    public void Error(string message)
    {
        Output($"Error: {message}");
    }

    /// <summary>
    /// Resolve special object references like "here" to actual object IDs.
    /// Also searches by name in inventory, equipment, and room.
    /// </summary>
    /// <param name="reference">The reference to resolve (e.g., "here", "me", item name, or object ID)</param>
    /// <returns>The resolved object ID, or null if the reference couldn't be resolved</returns>
    public string? ResolveObjectId(string reference)
    {
        // Special keywords
        if (string.Equals(reference, "here", StringComparison.OrdinalIgnoreCase))
        {
            return GetPlayerLocation();
        }

        if (string.Equals(reference, "me", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reference, "self", StringComparison.OrdinalIgnoreCase))
        {
            return PlayerId;
        }

        // If it looks like an object ID (contains # or .cs), return as-is
        if (reference.Contains('#') || reference.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return reference;
        }

        // Search by name in inventory, equipment, and room
        return FindObjectByName(reference);
    }

    /// <summary>
    /// Find an object by name in inventory, equipment, or room.
    /// </summary>
    private string? FindObjectByName(string name)
    {
        if (State.Objects is null) return null;

        var normalizedName = name.ToLowerInvariant();

        // Search inventory
        foreach (var itemId in State.Containers.GetContents(PlayerId))
        {
            var obj = State.Objects.Get<IMudObject>(itemId);
            if (obj is not null && MatchesName(obj, normalizedName))
                return itemId;
        }

        // Search equipment
        foreach (var (_, itemId) in State.Equipment.GetAllEquipped(PlayerId))
        {
            var obj = State.Objects.Get<IMudObject>(itemId);
            if (obj is not null && MatchesName(obj, normalizedName))
                return itemId;
        }

        // Search room contents
        var roomId = GetPlayerLocation();
        if (roomId is not null)
        {
            foreach (var objId in State.Containers.GetContents(roomId))
            {
                if (objId == PlayerId) continue;
                var obj = State.Objects.Get<IMudObject>(objId);
                if (obj is not null && MatchesName(obj, normalizedName))
                    return objId;
            }
        }

        return null;
    }

    /// <summary>
    /// Check if an object's name or aliases match the search term.
    /// </summary>
    private static bool MatchesName(IMudObject obj, string normalizedName)
    {
        // Check main name
        if (obj.Name.ToLowerInvariant().Contains(normalizedName))
            return true;

        // Check aliases if available
        if (obj is IItem item)
        {
            foreach (var alias in item.Aliases)
            {
                if (alias.ToLowerInvariant().Contains(normalizedName))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Trigger a room event to notify all ILlmNpc objects in the room.
    /// This allows NPCs to react to player actions like speech, emotes, arrivals, etc.
    /// </summary>
    /// <param name="roomEvent">The event that occurred.</param>
    /// <param name="roomId">The room where the event occurred.</param>
    public async Task TriggerRoomEventAsync(RoomEvent roomEvent, string roomId)
    {
        if (State.Objects is null) return;

        var contents = State.Containers.GetContents(roomId);
        foreach (var objId in contents)
        {
            // Don't notify the actor about their own action
            if (objId == roomEvent.ActorId) continue;

            var obj = State.Objects.Get<IMudObject>(objId);
            if (obj is ILlmNpc llmNpc)
            {
                // Persistent per-NPC memory promotion (best-effort, non-blocking)
                var memorySystem = State.MemorySystem;
                if (memorySystem is not null)
                {
                    try
                    {
                        var write = MemoryPromotionRules.TryCreateObserverMemory(State, objId, obj, roomEvent, roomId);
                        if (write is not null)
                        {
                            memorySystem.TryEnqueueMemoryWrite(write);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Memory] Promotion failed: {ex.Message}");
                    }
                }

                var ctx = CreateContext(objId);
                await llmNpc.OnRoomEventAsync(roomEvent, ctx);
            }
        }
    }

    /// <summary>
    /// Trigger a room event in the player's current room.
    /// </summary>
    /// <param name="roomEvent">The event that occurred.</param>
    public async Task TriggerRoomEventAsync(RoomEvent roomEvent)
    {
        var roomId = GetPlayerLocation();
        if (roomId is not null)
        {
            await TriggerRoomEventAsync(roomEvent, roomId);
        }
    }
}
