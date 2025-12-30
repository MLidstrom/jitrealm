using JitRealm.Mud;

namespace JitRealm.Mud.Commands.Social;

/// <summary>
/// Shout a message that can be heard in adjacent rooms.
/// </summary>
public class ShoutCommand : CommandBase
{
    public override string Name => "shout";
    public override IReadOnlyList<string> Aliases => new[] { "yell" };
    public override string Usage => "shout <message>";
    public override string Description => "Shout a message to adjacent rooms";
    public override string Category => "Social";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var message = JoinArgs(args);
        var playerName = context.GetPlayer()?.Name ?? "Someone";
        var roomId = context.GetPlayerLocation();

        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return Task.CompletedTask;
        }

        // Message to the player
        context.Output($"You shout: {message}");

        // Message to current room
        context.State.Messages.Enqueue(new MudMessage(
            context.PlayerId,
            null,
            MessageType.Say,
            $"shouts: {message}",
            roomId
        ));

        // Get adjacent rooms and send messages there too
        var currentRoom = context.GetCurrentRoom();
        if (currentRoom is not null)
        {
            foreach (var exit in currentRoom.Exits)
            {
                var adjacentRoomId = exit.Value;
                context.State.Messages.Enqueue(new MudMessage(
                    context.PlayerId,
                    null,
                    MessageType.Emote,
                    $"shouts from nearby: {message}",
                    adjacentRoomId
                ));
            }
        }

        return Task.CompletedTask;
    }
}
