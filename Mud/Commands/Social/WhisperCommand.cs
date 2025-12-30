using JitRealm.Mud;

namespace JitRealm.Mud.Commands.Social;

/// <summary>
/// Send a private message to another player.
/// </summary>
public class WhisperCommand : CommandBase
{
    public override string Name => "whisper";
    public override IReadOnlyList<string> Aliases => new[] { "tell", "msg" };
    public override string Usage => "whisper <player> <message>";
    public override string Description => "Send a private message to a player";
    public override string Category => "Social";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 2)) return Task.CompletedTask;

        var targetName = args[0];
        var message = JoinArgs(args, 1);

        // Find the target player by name
        var targetSession = context.State.Sessions.GetByPlayerName(targetName);
        if (targetSession is null)
        {
            context.Output($"Player '{targetName}' is not online.");
            return Task.CompletedTask;
        }

        var targetPlayerId = targetSession.PlayerId;
        if (targetPlayerId is null)
        {
            context.Output($"Player '{targetName}' is not available.");
            return Task.CompletedTask;
        }

        var playerName = context.GetPlayer()?.Name ?? "Someone";

        // Send private message
        context.State.Messages.Enqueue(new MudMessage(
            context.PlayerId,
            targetPlayerId,
            MessageType.Tell,
            message,
            null
        ));

        context.Output($"You whisper to {targetSession.PlayerName}: {message}");

        return Task.CompletedTask;
    }
}
