namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to instantly kill a target (for testing/debugging).
/// </summary>
public class ZapCommand : WizardCommandBase
{
    public override string Name => "zap";
    public override string[] Aliases => Array.Empty<string>();
    public override string Usage => "zap <target> [--force]";
    public override string Description => "Instantly kill a target (use --force for players)";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1))
            return Task.CompletedTask;

        var targetRef = args[0];
        var forceFlag = args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));

        // Resolve target
        var targetId = context.ResolveObjectId(targetRef);
        if (targetId is null)
        {
            // Try finding in room by name
            var roomId = context.GetPlayerLocation();
            if (roomId is not null)
            {
                targetId = FindLivingInRoom(context, targetRef.ToLowerInvariant(), roomId);
            }
        }

        if (targetId is null)
        {
            context.Output($"Cannot find target: {targetRef}");
            return Task.CompletedTask;
        }

        // Get as ILiving
        var living = context.State.Objects?.Get<ILiving>(targetId);
        if (living is null)
        {
            context.Output($"{targetRef} is not a living entity.");
            return Task.CompletedTask;
        }

        // Safety check for players
        var isPlayer = context.State.Objects?.Get<IPlayer>(targetId) is not null;
        if (isPlayer && !forceFlag)
        {
            context.Output($"Cannot zap player {living.Name} without --force flag.");
            context.Output("Use: zap <player> --force");
            return Task.CompletedTask;
        }

        // Check if already dead
        if (!living.IsAlive)
        {
            context.Output($"{living.Name} is already dead.");
            return Task.CompletedTask;
        }

        // Get state store and set HP to 0
        var stateStore = context.State.Objects?.GetStateStore(targetId);
        if (stateStore is null)
        {
            context.Output("Cannot access target state.");
            return Task.CompletedTask;
        }

        // Announce the zap
        context.Output($"You point your finger at {living.Name}...");

        // Set HP to 0
        stateStore.Set("hp", 0);

        // Call Die() to trigger death effects
        var targetCtx = context.CreateContext(targetId);
        living.Die(context.PlayerId, targetCtx);

        context.Output($"*ZAP* {living.Name} crumples to the ground!");

        return Task.CompletedTask;
    }

    private static string? FindLivingInRoom(CommandContext context, string name, string roomId)
    {
        var contents = context.State.Containers.GetContents(roomId);
        foreach (var objId in contents)
        {
            var obj = context.State.Objects?.Get<IMudObject>(objId);
            if (obj is null || obj is not ILiving living)
                continue;

            // Check name
            if (obj.Name.ToLowerInvariant().Contains(name))
                return objId;

            // Check aliases
            foreach (var alias in living.Aliases)
            {
                if (alias.ToLowerInvariant().Contains(name) ||
                    name.Contains(alias.ToLowerInvariant()))
                    return objId;
            }
        }
        return null;
    }
}
