namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to force an NPC to execute a command.
/// Useful for testing NPC behavior without waiting for AI decisions.
/// </summary>
public class ForceCommand : WizardCommandBase
{
    public override string Name => "force";
    public override string[] Aliases => new[] { "make", "compel" };
    public override string Usage => "force <npc> <command...>";
    public override string Description => "Make an NPC execute a command";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            context.Output("Usage: force <npc> <command...>");
            context.Output("Examples:");
            context.Output("  force tom say Hello everyone!");
            context.Output("  force shopkeeper give sword to player");
            context.Output("  force goblin go north");
            return;
        }

        var npcRef = args[0];
        var command = string.Join(" ", args.Skip(1));

        // Find the NPC
        var found = FindNpc(context, npcRef);
        if (found is null)
        {
            context.Output($"No NPC matching '{npcRef}' found.");
            context.Output("Hint: Use 'where' to find loaded NPCs, or visit a room to spawn them.");
            return;
        }

        var (npcId, npcName) = found.Value;

        // Check if it's a player (not allowed)
        var npc = context.State.Objects!.Get<IMudObject>(npcId);
        if (npc is IPlayer)
        {
            context.Output("Cannot force players.");
            return;
        }

        // Get the NPC's context
        var npcContext = context.CreateContext(npcId);

        // Show what we're doing
        context.Output($"Forcing {npcName} to: {command}");

        // Execute the command as the NPC
        var success = await npcContext.ExecuteCommandAsync(command);

        if (!success)
        {
            context.Output($"[{npcName}] Command failed or unrecognized: {command}");
        }
    }

    /// <summary>
    /// Find an NPC by name, alias, or instance ID.
    /// Searches current room first, then globally.
    /// </summary>
    private static (string instanceId, string name)? FindNpc(CommandContext context, string search)
    {
        var searchLower = search.ToLowerInvariant();

        // First: check if it's an exact instance ID
        if (search.Contains('#') || search.Contains('/'))
        {
            var obj = context.State.Objects!.Get<IMudObject>(search);
            if (obj is ILiving && obj is not IPlayer)
            {
                return (search, obj.Name ?? search);
            }
        }

        // Second: search current room (exact matches first)
        var currentRoom = context.GetPlayerLocation();
        if (currentRoom is not null)
        {
            var roomContents = context.State.Containers.GetContents(currentRoom);
            foreach (var objId in roomContents)
            {
                var obj = context.State.Objects!.Get<IMudObject>(objId);
                if (obj is not ILiving living || obj is IPlayer) continue;

                // Exact name match
                if (obj.Name?.Equals(searchLower, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return (objId, obj.Name);
                }

                // Exact alias match
                if (living.Aliases.Any(a => a.Equals(searchLower, StringComparison.OrdinalIgnoreCase)))
                {
                    return (objId, obj.Name ?? objId);
                }
            }

            // Partial matches in current room
            foreach (var objId in roomContents)
            {
                var obj = context.State.Objects!.Get<IMudObject>(objId);
                if (obj is not ILiving living || obj is IPlayer) continue;

                if (obj.Name?.ToLowerInvariant().Contains(searchLower) == true)
                {
                    return (objId, obj.Name);
                }

                if (living.Aliases.Any(a => a.ToLowerInvariant().Contains(searchLower)))
                {
                    return (objId, obj.Name ?? objId);
                }
            }
        }

        // Third: search globally (exact matches first)
        foreach (var instanceId in context.State.Objects!.ListInstanceIds())
        {
            var obj = context.State.Objects.Get<IMudObject>(instanceId);
            if (obj is not ILiving living || obj is IPlayer) continue;

            // Exact name match
            if (obj.Name?.Equals(searchLower, StringComparison.OrdinalIgnoreCase) == true)
            {
                return (instanceId, obj.Name);
            }

            // Exact alias match
            if (living.Aliases.Any(a => a.Equals(searchLower, StringComparison.OrdinalIgnoreCase)))
            {
                return (instanceId, obj.Name ?? instanceId);
            }
        }

        // Fourth: global partial matches
        foreach (var instanceId in context.State.Objects!.ListInstanceIds())
        {
            var obj = context.State.Objects.Get<IMudObject>(instanceId);
            if (obj is not ILiving living || obj is IPlayer) continue;

            if (obj.Name?.ToLowerInvariant().Contains(searchLower) == true)
            {
                return (instanceId, obj.Name);
            }

            if (living.Aliases.Any(a => a.ToLowerInvariant().Contains(searchLower)))
            {
                return (instanceId, obj.Name ?? instanceId);
            }
        }

        return null;
    }
}
