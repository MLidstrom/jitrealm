namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to list connected users with detailed information.
/// </summary>
public class UsersCommand : WizardCommandBase
{
    public override string Name => "users";
    public override string[] Aliases => new[] { "sessions", "connections" };
    public override string Usage => "users";
    public override string Description => "List connected users with details";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var sessions = context.State.Sessions.GetAll();

        if (sessions.Count == 0)
        {
            context.Output("No users connected.");
            return Task.CompletedTask;
        }

        context.Output($"Connected Users ({sessions.Count}):");
        context.Output("");

        var index = 1;
        foreach (var session in sessions)
        {
            var name = session.PlayerName ?? "(logging in)";
            var wizardFlag = session.IsWizard ? " (wizard)" : "";

            // Get location
            var locationName = "unknown";
            if (session.PlayerId is not null)
            {
                var roomId = context.State.Containers.GetContainer(session.PlayerId);
                if (roomId is not null)
                {
                    var room = context.State.Objects?.Get<IRoom>(roomId);
                    locationName = room?.Name ?? roomId;
                }
            }

            // Format output
            context.Output($"  [{index}] {name}{wizardFlag}");
            context.Output($"      Location: {locationName}");
            context.Output($"      Session: {session.SessionId}");

            if (session.PlayerId is not null)
            {
                context.Output($"      Player ID: {session.PlayerId}");
            }

            context.Output("");
            index++;
        }

        return Task.CompletedTask;
    }
}
