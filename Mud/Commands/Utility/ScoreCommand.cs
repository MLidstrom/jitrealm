namespace JitRealm.Mud.Commands.Utility;

/// <summary>
/// Display player's score and stats.
/// </summary>
public class ScoreCommand : CommandBase
{
    public override string Name => "score";
    public override IReadOnlyList<string> Aliases => new[] { "stats", "status" };
    public override string Usage => "score";
    public override string Description => "Show your stats";
    public override string Category => "Utility";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var player = context.GetPlayer();
        if (player is null)
        {
            context.Output("No player found.");
            return Task.CompletedTask;
        }

        // Use session's PlayerName since the object's Ctx isn't bound
        var playerName = context.Session.PlayerName ?? "Unknown";

        if (context.IsWizard)
        {
            // Wizard display - they transcend mortal concerns
            context.Output($"=== {playerName} the Wizard ===");
            context.Output("");
            context.Output("You are a Wizard - master of the realm.");
            context.Output("Mortal concerns like HP and experience do not apply to you.");
        }
        else
        {
            // Regular player display
            context.Output($"=== {playerName} ===");
            context.Output("");

            // Health
            var hpPercent = player.MaxHP > 0 ? (player.HP * 100 / player.MaxHP) : 0;
            var hpBar = CreateBar(player.HP, player.MaxHP, 20);
            context.Output($"HP: [{hpBar}] {player.HP}/{player.MaxHP} ({hpPercent}%)");

            // Level & Experience
            context.Output("");
            context.Output($"Level: {player.Level}");
            context.Output($"Experience: {player.Experience}");

            // Calculate XP to next level (same formula as PlayerBase)
            var xpForNext = CalculateXpForLevel(player.Level + 1);
            var xpNeeded = xpForNext - player.Experience;
            if (xpNeeded > 0)
            {
                context.Output($"XP to next level: {xpNeeded}");
            }

            // Equipment stats
            if (player is IHasEquipment equipped)
            {
                context.Output("");
                context.Output("Combat Stats:");
                var (minDmg, maxDmg) = equipped.WeaponDamage;
                context.Output($"  Weapon Damage: {minDmg}-{maxDmg}");
                context.Output($"  Armor Class: {equipped.TotalArmorClass}");
            }

            // Inventory capacity
            context.Output("");
            context.Output("Inventory:");
            context.Output($"  Carrying: {player.CarriedWeight}/{player.CarryCapacity} lbs");

            // Combat status
            if (context.State.Combat.IsInCombat(context.PlayerId))
            {
                var targetId = context.State.Combat.GetCombatTarget(context.PlayerId);
                var target = targetId is not null ? context.State.Objects?.Get<IMudObject>(targetId) : null;
                context.Output("");
                context.Output($"In combat with: {target?.Name ?? targetId}");
            }
        }

        // Session info (shown for both wizards and players)
        context.Output("");
        context.Output($"Session time: {FormatTimeSpan(player.SessionTime)}");

        if (player is PlayerBase playerBase)
        {
            context.Output($"Total playtime: {FormatTimeSpan(playerBase.TotalPlaytime)}");
        }

        return Task.CompletedTask;
    }

    private static string CreateBar(int current, int max, int width)
    {
        if (max <= 0) return new string('-', width);
        var filled = (int)((double)current / max * width);
        filled = Math.Clamp(filled, 0, width);
        return new string('#', filled) + new string('-', width - filled);
    }

    private static int CalculateXpForLevel(int level)
    {
        const int BaseXpPerLevel = 100;
        const double XpMultiplier = 1.5;
        if (level <= 1) return 0;
        return (int)(BaseXpPerLevel * Math.Pow(XpMultiplier, level - 2));
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
        {
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        }
        else if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        }
        else if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        }
        else
        {
            return $"{ts.Seconds}s";
        }
    }
}
