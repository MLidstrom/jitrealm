namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Display file contents command.
/// </summary>
public class CatCommand : WizardCommandBase
{
    public override string Name => "cat";
    public override string Usage => "cat <file>";
    public override string Description => "Display entire file with line numbers";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.Output("Usage: cat <file>");
            return;
        }

        var worldRoot = WizardFilesystem.GetWorldRoot(context);
        var sessionId = context.Session.SessionId;

        var filePath = string.Join(" ", args);
        var resolvedPath = WizardFilesystem.ResolvePath(sessionId, filePath, worldRoot);

        if (resolvedPath is null)
        {
            context.Output("Invalid path.");
            return;
        }

        var fsPath = WizardFilesystem.ToFilesystemPath(resolvedPath, worldRoot);

        if (!File.Exists(fsPath))
        {
            context.Output($"File not found: {resolvedPath}");
            return;
        }

        var lines = await File.ReadAllLinesAsync(fsPath);
        context.Output($"=== {resolvedPath} ({lines.Length} lines) ===");

        var lineNum = 1;
        foreach (var line in lines)
        {
            context.Output($"{lineNum,4}: {line}");
            lineNum++;
        }

        context.Output($"=== End of {resolvedPath} ===");
    }
}
