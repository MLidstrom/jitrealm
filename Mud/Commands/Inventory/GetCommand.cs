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

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var itemName = JoinArgs(args);
        var roomId = context.GetPlayerLocation();
        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return Task.CompletedTask;
        }

        // Find item in room
        var ctx = context.CreateContext(context.PlayerId);
        var itemId = ctx.FindItem(itemName, roomId);
        if (itemId is null)
        {
            context.Output($"You don't see '{itemName}' here.");
            return Task.CompletedTask;
        }

        // Check if it's a carryable item
        var item = context.State.Objects!.Get<IItem>(itemId);
        if (item is null)
        {
            context.Output("That's not an item.");
            return Task.CompletedTask;
        }

        // Check weight limit
        var player = context.GetPlayer();
        if (player is not null && !player.CanCarry(item.Weight))
        {
            context.Output($"You can't carry that much weight. (Carrying {player.CarriedWeight}/{player.CarryCapacity})");
            return Task.CompletedTask;
        }

        // Move item to player inventory
        ctx.Move(itemId, context.PlayerId);
        context.Output($"You pick up {item.ShortDescription}.");
        return Task.CompletedTask;
    }
}
