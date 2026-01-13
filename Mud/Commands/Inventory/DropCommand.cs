using JitRealm.Mud.AI;

namespace JitRealm.Mud.Commands.Inventory;

/// <summary>
/// Drop an item from inventory into the current room.
/// Supports partial stack operations: "drop 5 gold coins" drops only 5 from a larger stack.
/// </summary>
public class DropCommand : CommandBase
{
    public override string Name => "drop";
    public override IReadOnlyList<string> Aliases => Array.Empty<string>();
    public override string Usage => "drop [quantity] <item>";
    public override string Description => "Drop an item (or a quantity from a stack)";
    public override string Category => "Inventory";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var roomId = context.GetPlayerLocation();
        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return;
        }

        // Parse optional quantity prefix (e.g., "drop 5 gold coins")
        var (requestedQuantity, itemName) = ParseQuantityAndName(args);

        // Find item in inventory
        var ctx = context.CreateContext(context.PlayerId);
        var itemId = ctx.FindItem(itemName, context.PlayerId);
        if (itemId is null)
        {
            context.Output($"You're not carrying '{itemName}'.");
            return;
        }

        var item = context.State.Objects!.Get<IItem>(itemId);
        if (item is null)
        {
            context.Output("That's not an item.");
            return;
        }

        var playerName = context.Session.PlayerName ?? "Someone";
        var itemDisplayName = item.ShortDescription;

        // Handle stackable items - merge with existing piles in room
        if (item is IStackable stackable)
        {
            var currentAmount = stackable.Amount;
            var stackKey = stackable.StackKey;
            var blueprintId = StackHelper.GetBlueprintId(itemId);

            // Determine how many to drop
            var amountToDrop = requestedQuantity.HasValue
                ? Math.Min(requestedQuantity.Value, currentAmount)
                : currentAmount;

            if (amountToDrop <= 0)
            {
                context.Output("You can't drop zero items.");
                return;
            }

            if (amountToDrop >= currentAmount)
            {
                // Drop the entire stack
                context.State.Containers.Remove(itemId);
                await context.State.Objects!.DestructAsync(itemId, context.State);
            }
            else
            {
                // Partial drop - reduce the stack in inventory
                var stateStore = context.State.Objects.GetStateStore(itemId);
                stateStore?.Set("amount", currentAmount - amountToDrop);
            }

            // Add to room (will merge with existing pile)
            // For coins, preserve the material in the new stack
            Dictionary<string, object>? initialState = null;
            if (item is ICoin coin)
            {
                initialState = new Dictionary<string, object> { ["material"] = coin.Material.ToString() };
            }
            await StackHelper.AddStackToContainerAsync(context.State, roomId, stackKey, blueprintId, amountToDrop, initialState);

            // Format display name (coins have special formatting)
            if (item is ICoin coinForDisplay)
                itemDisplayName = CoinHelper.FormatCoins(amountToDrop, coinForDisplay.Material);
            else
                itemDisplayName = amountToDrop == 1 ? item.Name : $"{amountToDrop} {item.Name}";
        }
        else
        {
            // Non-stackable items ignore quantity
            ctx.Move(itemId, roomId);
        }

        context.Output($"You drop {itemDisplayName}.");

        // Trigger room event for NPC reactions
        await context.TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.ItemDropped,
            ActorId = context.PlayerId,
            ActorName = playerName,
            Target = itemDisplayName
        }, roomId);
    }

    /// <summary>
    /// Parse a quantity prefix from args if present.
    /// E.g., ["5", "gold", "coins"] -> (5, "gold coins")
    /// E.g., ["sword"] -> (null, "sword")
    /// </summary>
    private static (int? Quantity, string ItemName) ParseQuantityAndName(string[] args)
    {
        if (args.Length >= 2 && int.TryParse(args[0], out var qty) && qty > 0)
        {
            // First arg is a quantity
            return (qty, string.Join(" ", args.Skip(1)));
        }

        // No quantity prefix
        return (null, string.Join(" ", args));
    }
}
