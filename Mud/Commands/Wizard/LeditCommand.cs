namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Line-based file editor command (for terminals without full ANSI support).
/// </summary>
public class LeditCommand : WizardCommandBase
{
    public override string Name => "ledit";
    public override string Usage => "ledit <file> [line# [text]] | ledit <file> +line# <text> | ledit <file> -line#";
    public override string Description => "Line-based file editor (no ANSI required)";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.Output("Usage:");
            context.Output("  ledit <file>              - Show file with line numbers");
            context.Output("  ledit <file> <line#>      - Show specific line");
            context.Output("  ledit <file> <line#> <text> - Replace line with text");
            context.Output("  ledit <file> +<line#> <text> - Insert text after line");
            context.Output("  ledit <file> -<line#>     - Delete line");
            context.Output("  ledit <file> append <text> - Append line at end");
            return;
        }

        var worldRoot = WizardFilesystem.GetWorldRoot(context);
        var sessionId = context.Session.SessionId;

        // Parse file path (first argument)
        var filePath = args[0];
        var resolvedPath = WizardFilesystem.ResolvePath(sessionId, filePath, worldRoot);

        if (resolvedPath is null)
        {
            context.Output("Invalid path.");
            return;
        }

        var fsPath = WizardFilesystem.ToFilesystemPath(resolvedPath, worldRoot);

        // If only file specified, show file with line numbers
        if (args.Length == 1)
        {
            await ShowFileAsync(context, fsPath, resolvedPath);
            return;
        }

        // Parse operation
        var operation = args[1];

        // Check for append operation
        if (operation.Equals("append", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 3)
            {
                context.Output("Usage: ledit <file> append <text>");
                return;
            }
            var text = string.Join(" ", args.Skip(2));
            await AppendLineAsync(context, fsPath, resolvedPath, text);
            return;
        }

        // Check for insert operation (+line#)
        if (operation.StartsWith("+"))
        {
            if (!int.TryParse(operation.Substring(1), out var lineNum) || lineNum < 0)
            {
                context.Output("Invalid line number. Use +0 to insert at beginning.");
                return;
            }
            if (args.Length < 3)
            {
                context.Output("Usage: ledit <file> +<line#> <text>");
                return;
            }
            var text = string.Join(" ", args.Skip(2));
            await InsertLineAsync(context, fsPath, resolvedPath, lineNum, text);
            return;
        }

        // Check for delete operation (-line#)
        if (operation.StartsWith("-"))
        {
            if (!int.TryParse(operation.Substring(1), out var lineNum) || lineNum < 1)
            {
                context.Output("Invalid line number. Line numbers start at 1.");
                return;
            }
            await DeleteLineAsync(context, fsPath, resolvedPath, lineNum);
            return;
        }

        // Must be line number for view or replace
        if (!int.TryParse(operation, out var targetLine) || targetLine < 1)
        {
            context.Output("Invalid line number. Line numbers start at 1.");
            return;
        }

        // If just line number, show that line
        if (args.Length == 2)
        {
            await ShowLineAsync(context, fsPath, resolvedPath, targetLine);
            return;
        }

        // Replace line with text
        var replaceText = string.Join(" ", args.Skip(2));
        await ReplaceLineAsync(context, fsPath, resolvedPath, targetLine, replaceText);
    }

    private async Task ShowFileAsync(CommandContext context, string fsPath, string virtualPath)
    {
        if (!File.Exists(fsPath))
        {
            context.Output($"File not found: {virtualPath}");
            context.Output("Use 'ledit <file> append <text>' to create a new file.");
            return;
        }

        var lines = await File.ReadAllLinesAsync(fsPath);
        context.Output($"=== {virtualPath} ({lines.Length} lines) ===");

        for (var i = 0; i < lines.Length; i++)
        {
            context.Output($"{i + 1,4}: {lines[i]}");
        }

        context.Output($"=== End of {virtualPath} ===");
    }

    private async Task ShowLineAsync(CommandContext context, string fsPath, string virtualPath, int lineNum)
    {
        if (!File.Exists(fsPath))
        {
            context.Output($"File not found: {virtualPath}");
            return;
        }

        var lines = await File.ReadAllLinesAsync(fsPath);

        if (lineNum > lines.Length)
        {
            context.Output($"Line {lineNum} does not exist. File has {lines.Length} lines.");
            return;
        }

        context.Output($"{lineNum,4}: {lines[lineNum - 1]}");
    }

    private async Task ReplaceLineAsync(CommandContext context, string fsPath, string virtualPath, int lineNum, string text)
    {
        if (!File.Exists(fsPath))
        {
            context.Output($"File not found: {virtualPath}");
            return;
        }

        var lines = (await File.ReadAllLinesAsync(fsPath)).ToList();

        if (lineNum > lines.Count)
        {
            context.Output($"Line {lineNum} does not exist. File has {lines.Count} lines.");
            return;
        }

        var oldText = lines[lineNum - 1];
        lines[lineNum - 1] = text;

        await File.WriteAllLinesAsync(fsPath, lines);

        context.Output($"Replaced line {lineNum}:");
        context.Output($"  Old: {oldText}");
        context.Output($"  New: {text}");
    }

    private async Task InsertLineAsync(CommandContext context, string fsPath, string virtualPath, int afterLine, string text)
    {
        List<string> lines;

        if (File.Exists(fsPath))
        {
            lines = (await File.ReadAllLinesAsync(fsPath)).ToList();
        }
        else
        {
            // Create new file
            var dir = Path.GetDirectoryName(fsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                context.Output($"Directory does not exist: {Path.GetDirectoryName(virtualPath)}");
                return;
            }
            lines = new List<string>();
        }

        if (afterLine > lines.Count)
        {
            context.Output($"Cannot insert after line {afterLine}. File has {lines.Count} lines.");
            context.Output("Use 'ledit <file> append <text>' to add at end.");
            return;
        }

        lines.Insert(afterLine, text);
        await File.WriteAllLinesAsync(fsPath, lines);

        var newLineNum = afterLine + 1;
        context.Output($"Inserted line {newLineNum}:");
        context.Output($"{newLineNum,4}: {text}");
    }

    private async Task DeleteLineAsync(CommandContext context, string fsPath, string virtualPath, int lineNum)
    {
        if (!File.Exists(fsPath))
        {
            context.Output($"File not found: {virtualPath}");
            return;
        }

        var lines = (await File.ReadAllLinesAsync(fsPath)).ToList();

        if (lineNum > lines.Count)
        {
            context.Output($"Line {lineNum} does not exist. File has {lines.Count} lines.");
            return;
        }

        var deletedText = lines[lineNum - 1];
        lines.RemoveAt(lineNum - 1);

        await File.WriteAllLinesAsync(fsPath, lines);

        context.Output($"Deleted line {lineNum}:");
        context.Output($"  {deletedText}");
        context.Output($"File now has {lines.Count} lines.");
    }

    private async Task AppendLineAsync(CommandContext context, string fsPath, string virtualPath, string text)
    {
        List<string> lines;

        if (File.Exists(fsPath))
        {
            lines = (await File.ReadAllLinesAsync(fsPath)).ToList();
        }
        else
        {
            // Create new file
            var dir = Path.GetDirectoryName(fsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                context.Output($"Directory does not exist: {Path.GetDirectoryName(virtualPath)}");
                return;
            }
            lines = new List<string>();
            context.Output($"Creating new file: {virtualPath}");
        }

        lines.Add(text);
        await File.WriteAllLinesAsync(fsPath, lines);

        context.Output($"Appended line {lines.Count}:");
        context.Output($"{lines.Count,4}: {text}");
    }
}
