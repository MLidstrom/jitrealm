using JitRealm.Mud;
using JitRealm.Mud.AI;

namespace JitRealm.Mud.Commands.Social;

/// <summary>
/// Say a message to everyone in the current room.
/// </summary>
public class SayCommand : CommandBase
{
    public override string Name => "say";
    public override IReadOnlyList<string> Aliases => new[] { "'" };
    public override string Usage => "say <message>";
    public override string Description => "Say something to everyone in the room";
    public override string Category => "Social";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var message = JoinArgs(args);
        var roomId = context.GetPlayerLocation();

        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return;
        }

        var playerName = context.Session.PlayerName ?? "Someone";

        // Message to the player
        context.Output($"You say: {message}");

        // Message to the room
        context.State.Messages.Enqueue(new MudMessage(
            context.PlayerId,
            null,
            MessageType.Say,
            message,
            roomId
        ));

        // Trigger room event for NPC reactions
        await context.TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.Speech,
            ActorId = context.PlayerId,
            ActorName = playerName,
            Message = message
        }, roomId);
    }
}
