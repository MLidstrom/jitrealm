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

        // Move item to room
        ctx.Move(itemId, roomId);
        context.Output($"You drop {item.ShortDescription}.");

        // Trigger room event for NPC reactions
        await context.TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.ItemDropped,
            ActorId = context.PlayerId,
            ActorName = playerName,
            Target = item.ShortDescription
        }, roomId);
    }
}
