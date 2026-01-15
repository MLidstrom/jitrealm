namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to move objects between containers (rooms, inventories).
/// </summary>
public class MoveCommand : WizardCommandBase
{
    public override string Name => "move";
    public override string[] Aliases => new[] { "trans", "transfer" };
    public override string Usage => "move <object> to <destination>";
    public override string Description => "Move an object to another container";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length < 3)
        {
            ShowUsage(context);
            return Task.CompletedTask;
        }

        // Parse "move <object> to <destination>"
        var toIndex = Array.FindIndex(args, a => a.Equals("to", StringComparison.OrdinalIgnoreCase));
        if (toIndex < 1 || toIndex >= args.Length - 1)
        {
            ShowUsage(context);
            return Task.CompletedTask;
        }

        var objectRef = string.Join(" ", args.Take(toIndex));
        var destRef = string.Join(" ", args.Skip(toIndex + 1));

        // Resolve source object
        var objectId = context.ResolveObjectId(objectRef);
        if (objectId is null)
        {
            // Try finding by name in room
            var roomId = context.GetPlayerLocation();
            if (roomId is not null)
            {
                objectId = FindObjectInRoom(context, objectRef.ToLowerInvariant(), roomId);
            }
        }

        if (objectId is null)
        {
            context.Output($"Cannot find object: {objectRef}");
            return Task.CompletedTask;
        }

        // Get the object to display its name
        var obj = context.State.Objects?.Get<IMudObject>(objectId);
        var objName = obj?.Name ?? objectId;

        // Resolve destination
        string? destId = null;

        if (destRef.Equals("me", StringComparison.OrdinalIgnoreCase) ||
            destRef.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            destId = context.PlayerId;
        }
        else if (destRef.Equals("here", StringComparison.OrdinalIgnoreCase))
        {
            destId = context.GetPlayerLocation();
        }
        else
        {
            destId = context.ResolveObjectId(destRef);
        }

        // Try finding destination by name in room (for NPCs, containers)
        if (destId is null)
        {
            var roomId = context.GetPlayerLocation();
            if (roomId is not null)
            {
                destId = FindLivingOrContainerInRoom(context, destRef.ToLowerInvariant(), roomId);
            }
        }

        if (destId is null)
        {
            context.Output($"Cannot find destination: {destRef}");
            return Task.CompletedTask;
        }

        // Get destination name
        var destObj = context.State.Objects?.Get<IMudObject>(destId);
        var destName = destObj?.Name ?? destId;

        // Get current location for reporting
        var oldContainer = context.State.Containers.GetContainer(objectId);

        // Perform the move
        context.State.Containers.Move(objectId, destId);

        // Report success
        context.Output($"Moved {objName} to {destName}.");

        return Task.CompletedTask;
    }

    private void ShowUsage(CommandContext context)
    {
        context.Output("Usage: move <object> to <destination>");
        context.Output("");
        context.Output("Destinations can be:");
        context.Output("  me / self  - Your inventory");
        context.Output("  here       - Current room");
        context.Output("  <name>     - NPC, player, or container in room");
        context.Output("  <id>       - Object ID (e.g., Rooms/tavern.cs#000001)");
        context.Output("");
        context.Output("Examples:");
        context.Output("  move sword to me       - Move sword to your inventory");
        context.Output("  move goblin to here    - Move goblin to current room");
        context.Output("  move gold to shopkeeper - Move gold to NPC inventory");
    }

    private static string? FindObjectInRoom(CommandContext context, string name, string roomId)
    {
        var contents = context.State.Containers.GetContents(roomId);
        foreach (var objId in contents)
        {
            var obj = context.State.Objects?.Get<IMudObject>(objId);
            if (obj is null)
                continue;

            // Check name
            if (obj.Name.ToLowerInvariant().Contains(name))
                return objId;

            // Check aliases if it's an item
            if (obj is IItem item)
            {
                foreach (var alias in item.Aliases)
                {
                    if (alias.ToLowerInvariant().Contains(name))
                        return objId;
                }
            }

            // Check aliases if it's a living
            if (obj is ILiving living)
            {
                foreach (var alias in living.Aliases)
                {
                    if (alias.ToLowerInvariant().Contains(name))
                        return objId;
                }
            }
        }
        return null;
    }

    private static string? FindLivingOrContainerInRoom(CommandContext context, string name, string roomId)
    {
        var contents = context.State.Containers.GetContents(roomId);
        foreach (var objId in contents)
        {
            var obj = context.State.Objects?.Get<IMudObject>(objId);
            if (obj is null)
                continue;

            // Only match livings (NPCs, players) or containers
            if (obj is not ILiving && obj is not IContainer)
                continue;

            // Check name
            if (obj.Name.ToLowerInvariant().Contains(name))
                return objId;

            // Check aliases
            if (obj is ILiving living)
            {
                foreach (var alias in living.Aliases)
                {
                    if (alias.ToLowerInvariant().Contains(name))
                        return objId;
                }
            }
        }
        return null;
    }
}
