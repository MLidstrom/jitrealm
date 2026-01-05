namespace JitRealm.Mud.Commands.Combat;

/// <summary>
/// Evaluate a potential combat target.
/// </summary>
public class ConsiderCommand : CommandBase
{
    public override string Name => "consider";
    public override IReadOnlyList<string> Aliases => new[] { "con" };
    public override string Usage => "consider <target>";
    public override string Description => "Evaluate an opponent";
    public override string Category => "Combat";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var targetName = JoinArgs(args);
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

        var target = context.State.Objects!.Get<ILiving>(targetId);
        if (target is null)
        {
            context.Output("You can't fight that.");
            return Task.CompletedTask;
        }

        var player = context.State.Objects.Get<ILiving>(context.PlayerId);
        if (player is null)
        {
            context.Output("Error getting player stats.");
            return Task.CompletedTask;
        }

        // Compare levels/HP
        var playerPower = player.MaxHP;
        var targetPower = target.MaxHP;

        string difficulty;
        if (targetPower < playerPower * 0.5)
            difficulty = "an easy target";
        else if (targetPower < playerPower * 0.8)
            difficulty = "a fair fight";
        else if (targetPower < playerPower * 1.2)
            difficulty = "a challenging opponent";
        else if (targetPower < playerPower * 2.0)
            difficulty = "a dangerous foe";
        else
            difficulty = "certain death";

        context.Output($"{target.Name} looks like {difficulty}.");
        context.Output($"  HP: {target.HP}/{target.MaxHP}");

        if (target is IHasEquipment equipped)
        {
            context.Output($"  Armor Class: {equipped.TotalArmorClass}");
            var (min, max) = equipped.WeaponDamage;
            if (max > 0)
            {
                context.Output($"  Weapon Damage: {min}-{max}");
            }
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
            if (objId == context.PlayerId) continue;

            var obj = context.State.Objects.Get<IMudObject>(objId);
            if (obj is null) continue;

            if (obj.Name.ToLowerInvariant().Contains(normalizedName))
                return objId;
        }

        return null;
    }
}
