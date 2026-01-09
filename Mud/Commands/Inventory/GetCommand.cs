using JitRealm.Mud.AI;

namespace JitRealm.Mud.Commands.Inventory;

/// <summary>
/// Pick up an item from the current room.
/// </summary>
public class GetCommand : CommandBase
{
    public override string Name => "get";
    public override IReadOnlyList<string> Aliases => new[] { "take" };
    public override string Usage => "get <item>";
    public override string Description => "Pick up an item";
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

        // Find item in room
        var ctx = context.CreateContext(context.PlayerId);
        var itemId = ctx.FindItem(itemName, roomId);
        if (itemId is null)
        {
            context.Output($"You don't see '{itemName}' here.");
            return;
        }

        // Check if it's a carryable item
        var item = context.State.Objects!.Get<IItem>(itemId);
        if (item is null)
        {
            context.Output("That's not an item.");
            return;
        }

        // Check weight limit
        var player = context.GetPlayer();
        if (player is not null && !player.CanCarry(item.Weight))
        {
            context.Output($"You can't carry that much weight. (Carrying {player.CarriedWeight}/{player.CarryCapacity})");
            return;
        }

        var playerName = context.Session.PlayerName ?? "Someone";
        var itemDisplayName = item.ShortDescription;

        // Handle stackable items - merge with existing piles
        if (item is IStackable stackable)
        {
            var amount = stackable.Amount;
            var stackKey = stackable.StackKey;
            var blueprintId = StackHelper.GetBlueprintId(itemId);

            // Remove from room and destruct the item instance
            context.State.Containers.Remove(itemId);
            await context.State.Objects!.DestructAsync(itemId, context.State);

            // Add to player inventory (will merge with existing pile)
            await StackHelper.AddStackToContainerAsync(context.State, context.PlayerId, stackKey, blueprintId, amount);

            // Format display name (coins have special formatting)
            if (item is ICoin coin)
                itemDisplayName = CoinHelper.FormatCoins(amount, coin.Material);
        }
        else
        {
            // Move non-stackable item to player inventory
            ctx.Move(itemId, context.PlayerId);
        }

        context.Output($"You pick up {itemDisplayName}.");

        // Trigger room event for NPC reactions
        await context.TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.ItemTaken,
            ActorId = context.PlayerId,
            ActorName = playerName,
            Target = item.ShortDescription
        }, roomId);
    }
}
