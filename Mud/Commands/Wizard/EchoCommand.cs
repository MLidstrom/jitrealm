namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to send a message to a room without attribution.
/// </summary>
public class EchoCommand : WizardCommandBase
{
    public override string Name => "echo";
    public override string[] Aliases => Array.Empty<string>();
    public override string Usage => "echo [to <room>] <message>";
    public override string Description => "Send message to room without attribution";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1))
            return;

        string? targetRoomId = null;
        string message;

        // Check for "to <room>" syntax
        if (args.Length >= 3 && args[0].Equals("to", StringComparison.OrdinalIgnoreCase))
        {
            var roomRef = args[1];
            targetRoomId = context.ResolveObjectId(roomRef);

            if (targetRoomId is null)
            {
                // Try loading by blueprint ID
                var blueprintId = roomRef.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    ? roomRef
                    : roomRef + ".cs";

                try
                {
                    var room = await context.State.Objects!.LoadAsync<IRoom>(blueprintId, context.State);
                    targetRoomId = room?.Id;
                }
                catch
                {
                    // Room not found
                }
            }

            if (targetRoomId is null)
            {
                context.Output($"Cannot find room: {roomRef}");
                return;
            }

            message = string.Join(" ", args.Skip(2));
        }
        else
        {
            // Echo to current room
            targetRoomId = context.GetPlayerLocation();
            message = string.Join(" ", args);
        }

        if (targetRoomId is null)
        {
            context.Output("You are not in a room.");
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            context.Output("Echo what?");
            return;
        }

        // Get all sessions in the target room
        var sessions = context.State.Sessions.GetSessionsInRoom(
            targetRoomId,
            context.State.Containers.GetContainer
        );

        if (sessions.Count == 0)
        {
            context.Output("No players in that room to hear the message.");
            return;
        }

        // Send message to all players in room (including the wizard if they're there)
        foreach (var session in sessions)
        {
            await session.WriteLineAsync(message);
        }

        // Confirm to wizard if they're not in the target room
        var wizardRoom = context.GetPlayerLocation();
        if (wizardRoom != targetRoomId)
        {
            context.Output($"Echoed to room: {message}");
        }
    }
}
