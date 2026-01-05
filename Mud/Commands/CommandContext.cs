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
}
