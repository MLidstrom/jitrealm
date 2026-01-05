namespace JitRealm.Mud;

public interface IMudObject
{
    /// <summary>
    /// Stable object identifier (typically the path relative to World/).
    /// Example: Rooms/start.cs
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Short name of the object (e.g., "a rusty sword", "The Starting Room").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Long description shown when examining the object.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Detailed descriptions for parts of this object.
    /// Maps keywords to descriptions for "look at X" commands.
    /// Example: { "grass": "Soft green grass...", "sky": "A clear blue sky..." }
    /// </summary>
    IReadOnlyDictionary<string, string> Details { get; }

    /// <summary>
    /// Called after the object has been loaded and instantiated.
    /// Use this for initialization.
    /// </summary>
    void Create(WorldState state);
}
