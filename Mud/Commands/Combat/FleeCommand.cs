using JitRealm.Mud.Commands.Navigation;

namespace JitRealm.Mud.Commands.Combat;

/// <summary>
/// Attempt to flee from combat.
/// </summary>
public class FleeCommand : CommandBase
{
    public override string Name => "flee";
    public override IReadOnlyList<string> Aliases => new[] { "retreat" };
    public override string Usage => "flee";
    public override string Description => "Attempt to escape combat";
    public override string Category => "Combat";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!context.State.Combat.IsInCombat(context.PlayerId))
        {
            context.Output("You're not in combat.");
            return;
        }

        var exitDir = context.State.Combat.AttemptFlee(context.PlayerId, context.State, context.State.Clock);

        if (exitDir is null)
        {
            context.Output("You fail to escape!");
            return;
        }

        context.Output($"You flee to the {exitDir}!");

        // Actually move the player using GoCommand
        await new GoCommand().ExecuteAsync(context, new[] { exitDir });
    }
}
