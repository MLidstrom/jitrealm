using JitRealm.Mud;
using JitRealm.Mud.AI;

namespace JitRealm.Mud.Commands.Social;

/// <summary>
/// Perform a custom emote visible to everyone in the current room.
/// </summary>
public class EmoteCommand : CommandBase
{
    public override string Name => "emote";
    public override IReadOnlyList<string> Aliases => new[] { "em", ":" };
    public override string Usage => "emote <action>";
    public override string Description => "Perform a custom action visible to the room";
    public override string Category => "Social";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var action = JoinArgs(args);
        var roomId = context.GetPlayerLocation();

        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return;
        }

        var playerName = context.Session.PlayerName ?? "Someone";

        // Message to the player
        context.Output($"{playerName} {action}");

        // Message to the room
        context.State.Messages.Enqueue(new MudMessage(
            context.PlayerId,
            null,
            MessageType.Emote,
            action,
            roomId
        ));

        // Trigger room event for NPC reactions
        await context.TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.Emote,
            ActorId = context.PlayerId,
            ActorName = playerName,
            Message = action
        }, roomId);
    }
}
