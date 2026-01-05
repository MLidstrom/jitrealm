namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Change directory command.
/// </summary>
public class CdCommand : WizardCommandBase
{
    public override string Name => "cd";
    public override string Usage => "cd <path>";
    public override string Description => "Change working directory";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var worldRoot = WizardFilesystem.GetWorldRoot(context);
        var sessionId = context.Session.SessionId;

        if (args.Length == 0)
        {
            // cd with no args goes to root
            WizardFilesystem.SetWorkingDir(sessionId, "/");
            context.Output("/");
            return Task.CompletedTask;
        }

        var targetPath = string.Join(" ", args);
        var resolvedPath = WizardFilesystem.ResolvePath(sessionId, targetPath, worldRoot);

        if (resolvedPath is null)
        {
            context.Output("Invalid path - cannot leave World directory.");
            return Task.CompletedTask;
        }

        var fsPath = WizardFilesystem.ToFilesystemPath(resolvedPath, worldRoot);

        if (!Directory.Exists(fsPath))
        {
            context.Output($"Directory not found: {resolvedPath}");
            return Task.CompletedTask;
        }

        WizardFilesystem.SetWorkingDir(sessionId, resolvedPath);
        context.Output(resolvedPath);
        return Task.CompletedTask;
    }
}
