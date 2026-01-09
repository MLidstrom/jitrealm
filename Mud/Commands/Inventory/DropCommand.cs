using JitRealm.Mud.AI;

namespace JitRealm.Mud.Commands.Inventory;

/// <summary>
/// Drop an item from inventory into the current room.
/// </summary>
public class DropCommand : CommandBase
{
    public override string Name => "drop";
    public override IReadOnlyList<string> Aliases => Array.Empty<string>();
    public override string Usage => "drop <item>";
    public override string Description => "Drop an item";
    public override string Category => "Inventory";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var itemName = JoinArgs(args);
        var roomId = context.GetPlayerLocation();
        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return;
        }

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
            var amount = stackable.Amount;
            var stackKey = stackable.StackKey;
            var blueprintId = StackHelper.GetBlueprintId(itemId);

            // Remove from player and destruct the item instance
            context.State.Containers.Remove(itemId);
            await context.State.Objects!.DestructAsync(itemId, context.State);

            // Add to room (will merge with existing pile)
            await StackHelper.AddStackToContainerAsync(context.State, roomId, stackKey, blueprintId, amount);

            // Format display name (coins have special formatting)
            if (item is ICoin coin)
                itemDisplayName = CoinHelper.FormatCoins(amount, coin.Material);
        }
        else
        {
            // Move non-stackable item to room
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
}
