using JitRealm.Mud.AI;

namespace JitRealm.Mud.Commands.Inventory;

/// <summary>
/// Give an item from inventory to another player or NPC.
/// </summary>
public class GiveCommand : CommandBase
{
    public override string Name => "give";
    public override IReadOnlyList<string> Aliases => Array.Empty<string>();
    public override string Usage => "give <item> to <target>";
    public override string Description => "Give an item to someone";
    public override string Category => "Inventory";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 3)) return; // At minimum: "item to target"

        var roomId = context.GetPlayerLocation();
        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return;
        }

        // Parse "give <item> to <target>"
        var toIndex = Array.FindIndex(args, a => a.Equals("to", StringComparison.OrdinalIgnoreCase));
        if (toIndex < 1 || toIndex >= args.Length - 1)
        {
            context.Output("Usage: give <item> to <target>");
            return;
        }

        var itemName = string.Join(" ", args.Take(toIndex));
        var targetName = string.Join(" ", args.Skip(toIndex + 1));

        // Find item in player's inventory
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

        // Find target in room
        var targetId = FindLivingInRoom(context, targetName.ToLowerInvariant(), roomId);
        if (targetId is null)
        {
            context.Output($"You don't see '{targetName}' here.");
            return;
        }

        var target = context.State.Objects.Get<IMudObject>(targetId);
        if (target is null)
        {
            context.Output($"You don't see '{targetName}' here.");
            return;
        }

        var playerName = context.Session.PlayerName ?? "Someone";
        var itemDisplayName = item.ShortDescription;

        // Move item to target's inventory
        context.State.Containers.Move(itemId, targetId);

        // Call OnGive hook if item is carryable
        if (item is ICarryable carryable)
        {
            var itemCtx = context.CreateContext(itemId);
            carryable.OnGive(itemCtx, context.PlayerId, targetId);
        }

        context.Output($"You give {itemDisplayName} to {target.Name}.");

        // Message to room
        context.State.Messages.Enqueue(new MudMessage(
            context.PlayerId,
            null,
            MessageType.Emote,
            $"gives {itemDisplayName} to {target.Name}.",
            roomId
        ));

        // Trigger room event for NPC reactions
        await context.TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.ItemGiven,
            ActorId = context.PlayerId,
            ActorName = playerName,
            Target = target.Name,
            Message = itemDisplayName
        }, roomId);
    }

    private static string? FindLivingInRoom(CommandContext context, string name, string roomId)
    {
        var contents = context.State.Containers.GetContents(roomId);
        foreach (var objId in contents)
        {
            if (objId == context.PlayerId) continue;

            var obj = context.State.Objects?.Get<IMudObject>(objId);
            if (obj is null) continue;

            // Check if it's a living entity
            if (obj is not ILiving living) continue;

            // Check name match
            if (obj.Name.ToLowerInvariant().Contains(name))
                return objId;

            // Check aliases (e.g., "barnaby" for shopkeeper)
            foreach (var alias in living.Aliases)
            {
                if (alias.ToLowerInvariant().Contains(name) ||
                    name.Contains(alias.ToLowerInvariant()))
                    return objId;
            }

            // Check for player names in session format
            if (objId.StartsWith("session:"))
            {
                var playerPart = objId.Split(':').LastOrDefault()?.ToLowerInvariant();
                if (playerPart is not null && playerPart.Contains(name))
                    return objId;
            }
        }

        return null;
    }
}
