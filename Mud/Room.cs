namespace JitRealm.Mud;

public interface IRoom : IMudObject
{
    new string Description { get; }
    IReadOnlyDictionary<string, string> Exits { get; }

    /// <summary>
    /// Exits that are hidden and not shown in room descriptions.
    /// Players can still use these exits if they know about them.
    /// </summary>
    IReadOnlySet<string> HiddenExits { get; }

    IReadOnlyList<string> Contents { get; }

    /// <summary>
    /// Whether this room is outdoors (affects weather, time of day descriptions).
    /// </summary>
    bool IsOutdoors { get; }

    /// <summary>
    /// Whether this room is lit. Dark rooms require a light source.
    /// </summary>
    bool IsLit { get; }

    /// <summary>
    /// Alternative names for this room (for location matching).
    /// Examples: "shop", "store", "general store" for a shop room.
    /// </summary>
    IReadOnlyList<string> Aliases { get; }
}
