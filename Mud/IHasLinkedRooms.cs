namespace JitRealm.Mud;

/// <summary>
/// Interface for rooms that have linked rooms which should be loaded
/// when the room becomes active (e.g., storage rooms, hidden areas).
/// Linked rooms will have their spawns processed when any player enters.
/// </summary>
public interface IHasLinkedRooms
{
    /// <summary>
    /// Blueprint IDs of rooms that should be loaded when this room is activated.
    /// These rooms will have their spawns processed.
    /// </summary>
    IReadOnlyList<string> LinkedRooms { get; }
}
