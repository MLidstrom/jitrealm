using JitRealm.Mud;

namespace JitRealm.Mud.Commands.Social;

/// <summary>
/// List all online players.
/// </summary>
public class WhoCommand : CommandBase
{
    public override string Name => "who";
    public override IReadOnlyList<string> Aliases => new[] { "players", "online" };
    public override string Usage => "who";
    public override string Description => "List online players";
    public override string Category => "Social";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var sessions = context.State.Sessions.GetAll();
        var connectedPlayers = sessions
            .Where(s => s.IsConnected && s.PlayerName is not null)
            .ToList();

        if (connectedPlayers.Count == 0)
        {
            context.Output("No players are online.");
            return Task.CompletedTask;
        }

        context.Output("=== Online Players ===");
        foreach (var session in connectedPlayers)
        {
            var wizMark = session.IsWizard ? " [Wizard]" : "";
            var playerId = session.PlayerId;
            var player = playerId is not null
                ? context.State.Objects?.Get<IPlayer>(playerId)
                : null;

            var levelStr = player is not null ? $" (Level {player.Level})" : "";
            context.Output($"  {session.PlayerName}{levelStr}{wizMark}");
        }

        context.Output($"\nTotal: {connectedPlayers.Count} player(s) online");

        return Task.CompletedTask;
    }
}
