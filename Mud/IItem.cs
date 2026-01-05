namespace JitRealm.Mud;

/// <summary>
/// Base interface for all items in the world.
/// Items are objects that can exist in rooms or inventories.
/// </summary>
public interface IItem : IMudObject
{
    /// <summary>
    /// The weight of this item in arbitrary units.
    /// </summary>
    int Weight { get; }

    /// <summary>
    /// The monetary value of this item.
    /// </summary>
    int Value { get; }

    /// <summary>
    /// Brief description shown in room/inventory lists.
    /// Example: "a rusty sword"
    /// </summary>
    string ShortDescription { get; }

    /// <summary>
    /// Alternative names/keywords for this item used in player commands.
    /// Players can use any of these words to refer to the item.
    /// Example: ["sword", "rusty sword", "blade", "weapon"]
    /// </summary>
    IReadOnlyList<string> Aliases { get; }
}

/// <summary>
/// Interface for items that can be picked up, dropped, and transferred.
/// </summary>
public interface ICarryable : IItem
{
    /// <summary>
    /// Called when the item is picked up by someone.
    /// </summary>
    /// <param name="ctx">The MUD context.</param>
    /// <param name="pickerId">ID of who picked up the item.</param>
    void OnGet(IMudContext ctx, string pickerId);

    /// <summary>
    /// Called when the item is dropped.
    /// </summary>
    /// <param name="ctx">The MUD context.</param>
    /// <param name="dropperId">ID of who dropped the item.</param>
    void OnDrop(IMudContext ctx, string dropperId);

    /// <summary>
    /// Called when the item is given to someone else.
    /// </summary>
    /// <param name="ctx">The MUD context.</param>
    /// <param name="giverId">ID of who gave the item.</param>
    /// <param name="receiverId">ID of who received the item.</param>
    void OnGive(IMudContext ctx, string giverId, string receiverId);
}

/// <summary>
/// Interface for container items that can hold other items.
/// </summary>
public interface IContainer : IItem
{
    /// <summary>
    /// Maximum total weight this container can hold.
    /// </summary>
    int MaxCapacity { get; }

    /// <summary>
    /// Whether the container is currently open (items accessible).
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Open the container. Returns true if successful.
    /// </summary>
    bool Open(IMudContext ctx);

    /// <summary>
    /// Close the container. Returns true if successful.
    /// </summary>
    bool Close(IMudContext ctx);
}

/// <summary>
/// Interface for objects (typically livings) that can carry items.
/// </summary>
public interface IHasInventory
{
    /// <summary>
    /// Maximum total weight this entity can carry.
    /// </summary>
    int CarryCapacity { get; }

    /// <summary>
    /// Current total weight of carried items.
    /// </summary>
    int CarriedWeight { get; }

    /// <summary>
    /// Check if this entity can carry an additional item of the given weight.
    /// </summary>
    bool CanCarry(int weight);
}
