namespace JitRealm.Mud;

/// <summary>
/// Utility methods for stackable item manipulation.
/// Handles finding, adding, and merging stackable items in containers.
/// </summary>
public static class StackHelper
{
    /// <summary>
    /// Find a stackable item pile with matching StackKey in a container.
    /// </summary>
    public static string? FindStack(WorldState state, string containerId, string stackKey)
    {
        var contents = state.Containers.GetContents(containerId);
        foreach (var itemId in contents)
        {
            var stackable = state.Objects?.Get<IStackable>(itemId);
            if (stackable?.StackKey == stackKey)
                return itemId;
        }
        return null;
    }

    /// <summary>
    /// Add stackable items to a container, merging with existing piles of same StackKey.
    /// Returns the stack instance ID.
    /// </summary>
    public static async Task<string?> AddStackAsync(
        WorldState state,
        string containerId,
        string blueprintId,
        string stackKey,
        int amount)
    {
        if (amount <= 0 || state.Objects is null)
            return null;

        var existingId = FindStack(state, containerId, stackKey);
        if (existingId is not null)
        {
            // Merge with existing pile
            var itemState = state.Objects.GetStateStore(existingId);
            var existingAmount = itemState?.Get<int>("amount") ?? 1;
            itemState?.Set("amount", existingAmount + amount);
            return existingId;
        }
        else
        {
            // Create new stack instance
            var newItem = await state.Objects.CloneAsync<IStackable>(blueprintId, state);
            var newItemState = state.Objects.GetStateStore(newItem.Id);
            newItemState?.Set("amount", amount);
            state.Containers.Add(containerId, newItem.Id);
            return newItem.Id;
        }
    }

    /// <summary>
    /// Transfer stackable items from one container to another, merging at destination.
    /// Returns true if successful.
    /// </summary>
    public static async Task<bool> TransferStackAsync(
        WorldState state,
        string fromId,
        string toId,
        IStackable sourceStack,
        string sourceItemId,
        int amount)
    {
        if (amount <= 0 || amount > sourceStack.Amount)
            return false;

        var fromState = state.Objects!.GetStateStore(sourceItemId);
        var currentAmount = fromState?.Get<int>("amount") ?? 1;

        if (amount == currentAmount)
        {
            // Move entire stack - remove from source, add to destination with merge
            state.Containers.Remove(sourceItemId);

            // Check if there's an existing stack to merge with at destination
            var existingId = FindStack(state, toId, sourceStack.StackKey);
            if (existingId != null)
            {
                // Merge into existing stack
                var existingState = state.Objects.GetStateStore(existingId);
                var existingAmount = existingState?.Get<int>("amount") ?? 1;
                existingState?.Set("amount", existingAmount + amount);

                // Destruct the source item
                await state.Objects.DestructAsync(sourceItemId, state);
            }
            else
            {
                // No existing stack - just move the item
                state.Containers.Add(toId, sourceItemId);
            }
        }
        else
        {
            // Split: reduce source stack, add to destination
            fromState?.Set("amount", currentAmount - amount);

            // Get blueprint from source item
            var blueprintId = GetBlueprintId(sourceItemId);

            // Add to destination (will merge with existing if present)
            await AddStackToContainerAsync(state, toId, sourceStack.StackKey, blueprintId, amount);
        }

        return true;
    }

    /// <summary>
    /// Add items to a container, handling stackable merging automatically.
    /// For stackable items, merges with existing stacks.
    /// For non-stackable items, just adds to container.
    /// </summary>
    public static async Task AddStackToContainerAsync(
        WorldState state,
        string containerId,
        string stackKey,
        string blueprintId,
        int amount)
    {
        var existingId = FindStack(state, containerId, stackKey);
        if (existingId != null)
        {
            // Merge with existing
            var existingState = state.Objects!.GetStateStore(existingId);
            var existingAmount = existingState?.Get<int>("amount") ?? 1;
            existingState?.Set("amount", existingAmount + amount);
        }
        else
        {
            // Create new stack
            var newItem = await state.Objects!.CloneAsync<IStackable>(blueprintId, state);
            var newItemState = state.Objects.GetStateStore(newItem.Id);
            newItemState?.Set("amount", amount);
            state.Containers.Add(containerId, newItem.Id);
        }
    }

    /// <summary>
    /// Format a stackable item for display (e.g., "3 rusty swords", "1 health potion").
    /// </summary>
    public static string FormatStack(IStackable item)
    {
        if (item.Amount == 1)
            return item.ShortDescription;

        // For amounts > 1, use the item's ShortDescription which should handle pluralization
        return item.ShortDescription;
    }

    /// <summary>
    /// Extract blueprint ID from an instance ID.
    /// Instance IDs are formatted as "blueprint#instanceNumber".
    /// </summary>
    public static string GetBlueprintId(string instanceId)
    {
        var hashIndex = instanceId.IndexOf('#');
        return hashIndex >= 0 ? instanceId[..hashIndex] : instanceId;
    }
}
