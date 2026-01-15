namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Print working directory command.
/// </summary>
public class PwdCommand : WizardCommandBase
{
    public override string Name => "pwd";
    public override string[] Aliases => new[] { "cwd" };
    public override string Usage => "pwd";
    public override string Description => "Print current working directory";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var cwd = WizardFilesystem.GetWorkingDir(context.Session.SessionId);
        context.Output(cwd);
        return Task.CompletedTask;
    }
}
