namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Display file with paging command.
/// </summary>
public class MoreCommand : WizardCommandBase
{
    public override string Name => "more";
    public override string Usage => "more <file> [start] [lines]";
    public override string Description => "Display file with paging (default: 20 lines)";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.Output("Usage: more <file> [start_line] [num_lines]");
            context.Output("  start_line: Line to start from (default: 1)");
            context.Output("  num_lines: Number of lines to show (default: 20)");
            return;
        }

        var worldRoot = WizardFilesystem.GetWorldRoot(context);
        var sessionId = context.Session.SessionId;

        // Parse arguments: file [start] [lines]
        var filePath = args[0];
        var startLine = 1;
        var numLines = 20;

        if (args.Length >= 2 && int.TryParse(args[1], out var parsedStart))
        {
            startLine = Math.Max(1, parsedStart);
        }

        if (args.Length >= 3 && int.TryParse(args[2], out var parsedNum))
        {
            numLines = Math.Max(1, Math.Min(100, parsedNum)); // Cap at 100 lines
        }

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
        var totalLines = lines.Length;
        var endLine = Math.Min(startLine + numLines - 1, totalLines);

        context.Output($"=== {resolvedPath} (lines {startLine}-{endLine} of {totalLines}) ===");

        for (var i = startLine - 1; i < endLine && i < lines.Length; i++)
        {
            context.Output($"{i + 1,4}: {lines[i]}");
        }

        if (endLine < totalLines)
        {
            context.Output($"=== More: 'more {filePath} {endLine + 1}' for next page ===");
        }
        else
        {
            context.Output($"=== End of {resolvedPath} ===");
        }
    }
}
