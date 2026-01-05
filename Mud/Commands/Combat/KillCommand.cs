namespace JitRealm.Mud.Commands.Combat;

/// <summary>
/// Start combat with a target.
/// </summary>
public class KillCommand : CommandBase
{
    public override string Name => "kill";
    public override IReadOnlyList<string> Aliases => new[] { "attack" };
    public override string Usage => "kill <target>";
    public override string Description => "Attack a target";
    public override string Category => "Combat";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var targetName = JoinArgs(args);

        // Check if already in combat
        if (context.State.Combat.IsInCombat(context.PlayerId))
        {
            var currentTarget = context.State.Combat.GetCombatTarget(context.PlayerId);
            var currentTargetObj = context.State.Objects!.Get<IMudObject>(currentTarget!);
            context.Output($"You are already fighting {currentTargetObj?.Name ?? currentTarget}!");
            return Task.CompletedTask;
        }

        var roomId = context.GetPlayerLocation();
        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return Task.CompletedTask;
        }

        // Find target in room
        var targetId = FindTargetInRoom(context, targetName, roomId);
        if (targetId is null)
        {
            context.Output($"You don't see '{targetName}' here.");
            return Task.CompletedTask;
        }

        // Can't attack yourself
        if (targetId == context.PlayerId)
        {
            context.Output("You can't attack yourself!");
            return Task.CompletedTask;
        }

        // Check if target is a living thing
        var target = context.State.Objects!.Get<ILiving>(targetId);
        if (target is null)
        {
            context.Output("You can't attack that.");
            return Task.CompletedTask;
        }

        if (!target.IsAlive)
        {
            context.Output($"{target.Name} is already dead.");
            return Task.CompletedTask;
        }

        // Start combat
        context.State.Combat.StartCombat(context.PlayerId, targetId, context.State.Clock.Now);
        context.Output($"You attack {target.Name}!");

        // If target is not already in combat, they fight back
        if (!context.State.Combat.IsInCombat(targetId))
        {
            context.State.Combat.StartCombat(targetId, context.PlayerId, context.State.Clock.Now);
        }

        return Task.CompletedTask;
    }

    private static string? FindTargetInRoom(CommandContext context, string name, string roomId)
    {
        if (context.State.Objects is null) return null;

        var normalizedName = name.ToLowerInvariant();
        var contents = context.State.Containers.GetContents(roomId);

        foreach (var objId in contents)
        {
            if (objId == context.PlayerId) continue;  // Skip self

            var obj = context.State.Objects.Get<IMudObject>(objId);
            if (obj is null) continue;

            if (obj.Name.ToLowerInvariant().Contains(normalizedName))
                return objId;
        }

        return null;
    }
}
