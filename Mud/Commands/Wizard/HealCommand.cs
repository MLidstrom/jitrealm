namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to heal a target (player, NPC, or monster).
/// </summary>
public class HealCommand : WizardCommandBase
{
    public override string Name => "heal";
    public override string[] Aliases => Array.Empty<string>();
    public override string Usage => "heal <target> [amount|full]";
    public override string Description => "Restore HP to a target";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1))
            return Task.CompletedTask;

        var targetRef = args[0];
        var amountArg = args.Length > 1 ? args[1] : "full";

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

        // Get state store for HP manipulation
        var stateStore = context.State.Objects?.GetStateStore(targetId);
        if (stateStore is null)
        {
            context.Output("Cannot access target state.");
            return Task.CompletedTask;
        }

        var oldHp = living.HP;
        var maxHp = living.MaxHP;

        // Determine heal amount
        int healAmount;
        if (amountArg.Equals("full", StringComparison.OrdinalIgnoreCase))
        {
            healAmount = maxHp - oldHp;
        }
        else if (int.TryParse(amountArg, out var parsed))
        {
            healAmount = parsed;
        }
        else
        {
            context.Output($"Invalid amount: {amountArg}. Use a number or 'full'.");
            return Task.CompletedTask;
        }

        if (healAmount <= 0 && oldHp >= maxHp)
        {
            context.Output($"{living.Name} is already at full health ({oldHp}/{maxHp} HP).");
            return Task.CompletedTask;
        }

        // Apply healing
        var newHp = Math.Min(maxHp, oldHp + healAmount);
        stateStore.Set("hp", newHp);

        var actualHealed = newHp - oldHp;
        context.Output($"Healed {living.Name} for {actualHealed} HP ({oldHp} -> {newHp}/{maxHp}).");

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
