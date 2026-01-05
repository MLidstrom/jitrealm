namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// List directory contents command.
/// </summary>
public class LsCommand : WizardCommandBase
{
    public override string Name => "ls";
    public override IReadOnlyList<string> Aliases => new[] { "dir" };
    public override string Usage => "ls [path]";
    public override string Description => "List directory contents";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var worldRoot = WizardFilesystem.GetWorldRoot(context);
        var sessionId = context.Session.SessionId;

        // Determine target path
        var targetVirtualPath = args.Length > 0
            ? WizardFilesystem.ResolvePath(sessionId, string.Join(" ", args), worldRoot)
            : WizardFilesystem.GetWorkingDir(sessionId);

        if (targetVirtualPath is null)
        {
            context.Output("Invalid path.");
            return Task.CompletedTask;
        }

        var targetFsPath = WizardFilesystem.ToFilesystemPath(targetVirtualPath, worldRoot);

        if (!Directory.Exists(targetFsPath))
        {
            // Maybe it's a file?
            if (File.Exists(targetFsPath))
            {
                var fileName = Path.GetFileName(targetFsPath);
                context.Output(fileName);
                return Task.CompletedTask;
            }

            context.Output($"Directory not found: {targetVirtualPath}");
            return Task.CompletedTask;
        }

        var lines = new List<string> { $"Contents of {targetVirtualPath}:" };

        // List directories first
        var dirs = Directory.GetDirectories(targetFsPath)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .OrderBy(n => n);

        foreach (var dir in dirs)
        {
            lines.Add($"  {dir}/");
        }

        // Then list files
        var files = Directory.GetFiles(targetFsPath)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .OrderBy(n => n);

        foreach (var file in files)
        {
            lines.Add($"  {file}");
        }

        if (lines.Count == 1)
        {
            lines.Add("  (empty)");
        }

        context.Output(string.Join("\n", lines));
        return Task.CompletedTask;
    }
}
