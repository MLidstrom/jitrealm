namespace JitRealm.Mud;

public interface IMudObject
{
    /// <summary>
    /// Stable object identifier (typically the path relative to World/).
    /// Example: Rooms/start.cs
    /// </summary>
    string Id { get; }

    string Name { get; }

    /// <summary>
    /// Called after the object has been loaded and instantiated.
    /// Use this for initialization.
    /// </summary>
    void Create(WorldState state);
}
