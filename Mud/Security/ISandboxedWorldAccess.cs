namespace JitRealm.Mud.Security;

/// <summary>
/// Safe world access interface for world code.
/// Provides read-only queries without exposing ObjectManager, SessionManager, or raw schedulers.
/// This is the sandboxed replacement for direct WorldState access.
/// </summary>
public interface ISandboxedWorldAccess
{
    /// <summary>
    /// Gets an object by its ID (read-only access).
    /// </summary>
    /// <typeparam name="T">The type to cast to (must implement IMudObject).</typeparam>
    /// <param name="id">The object ID.</param>
    /// <returns>The object or null if not found.</returns>
    T? GetObject<T>(string id) where T : class, IMudObject;

    /// <summary>
    /// Lists all loaded object IDs.
    /// </summary>
    IEnumerable<string> ListObjectIds();

    /// <summary>
    /// Gets the contents of a room (object IDs contained in the room).
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>Collection of object IDs in the room.</returns>
    IReadOnlyCollection<string> GetRoomContents(string roomId);

    /// <summary>
    /// Gets the location (container ID) of an object.
    /// </summary>
    /// <param name="objectId">The object ID.</param>
    /// <returns>The container ID or null if not in a container.</returns>
    string? GetObjectLocation(string objectId);

    /// <summary>
    /// Gets the IDs of players currently in a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>Enumerable of player IDs in the room.</returns>
    IEnumerable<string> GetPlayersInRoom(string roomId);

    /// <summary>
    /// Gets the current time.
    /// </summary>
    DateTimeOffset Now { get; }

    /// <summary>
    /// Gets all equipped items for a living being.
    /// </summary>
    /// <param name="livingId">The living's object ID.</param>
    /// <returns>Dictionary of slot to item ID.</returns>
    IReadOnlyDictionary<EquipmentSlot, string> GetEquipment(string livingId);

    /// <summary>
    /// Gets the item equipped in a specific slot.
    /// </summary>
    /// <param name="livingId">The living's object ID.</param>
    /// <param name="slot">The equipment slot.</param>
    /// <returns>The item ID or null if nothing equipped.</returns>
    string? GetEquippedInSlot(string livingId, EquipmentSlot slot);
}
