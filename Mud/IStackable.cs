namespace JitRealm.Mud;

/// <summary>
/// Interface for items that can stack (multiple identical items in one pile).
/// When stackable items with the same StackKey are placed in the same container, they merge.
/// </summary>
public interface IStackable : IItem
{
    /// <summary>
    /// The number of items in this stack.
    /// </summary>
    int Amount { get; }

    /// <summary>
    /// Key used to identify items that can merge together.
    /// Items with the same StackKey in the same container will merge.
    /// Default implementation uses the blueprint ID.
    /// </summary>
    string StackKey { get; }
}
