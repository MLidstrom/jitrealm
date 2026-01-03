using System.Collections.Concurrent;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Tracks wizard working directories per session.
/// </summary>
public static class WizardFilesystem
{
    private static readonly ConcurrentDictionary<string, string> _workingDirs = new();

    /// <summary>
    /// Get the wizard's current working directory (relative to World/).
    /// Returns "/" if not set.
    /// </summary>
    public static string GetWorkingDir(string sessionId)
    {
        return _workingDirs.GetValueOrDefault(sessionId, "/");
    }

    /// <summary>
    /// Set the wizard's current working directory (relative to World/).
    /// </summary>
    public static void SetWorkingDir(string sessionId, string path)
    {
        _workingDirs[sessionId] = path;
    }

    /// <summary>
    /// Clear the working directory when a session ends.
    /// </summary>
    public static void ClearSession(string sessionId)
    {
        _workingDirs.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Get the absolute filesystem path for the World directory.
    /// </summary>
    public static string GetWorldRoot(CommandContext context)
    {
        var worldDir = context.State.Objects?.WorldRoot ?? "World";
        return Path.GetFullPath(worldDir);
    }

    /// <summary>
    /// Resolve a path relative to the wizard's current working directory.
    /// Returns the normalized path relative to World/, or null if the path escapes World/.
    /// </summary>
    public static string? ResolvePath(string sessionId, string path, string worldRoot)
    {
        var cwd = GetWorkingDir(sessionId);

        // Handle absolute paths (starting with /)
        string targetPath;
        if (path.StartsWith("/"))
        {
            targetPath = path;
        }
        else
        {
            // Relative path - combine with cwd
            targetPath = cwd == "/" ? "/" + path : cwd + "/" + path;
        }

        // Normalize the path (resolve . and ..)
        var parts = targetPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new Stack<string>();

        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }
            else if (part == "..")
            {
                if (stack.Count > 0)
                    stack.Pop();
                // If stack is empty, we're at root - ignore the ..
            }
            else
            {
                stack.Push(part);
            }
        }

        // Build the normalized path
        var normalizedParts = stack.Reverse().ToArray();
        var normalizedPath = "/" + string.Join("/", normalizedParts);

        // Verify the path doesn't escape World/
        var fullPath = Path.GetFullPath(Path.Combine(worldRoot, string.Join(Path.DirectorySeparatorChar.ToString(), normalizedParts)));
        if (!fullPath.StartsWith(worldRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null; // Path escapes World/
        }

        return normalizedPath;
    }

    /// <summary>
    /// Convert a virtual path (relative to World/) to an absolute filesystem path.
    /// </summary>
    public static string ToFilesystemPath(string virtualPath, string worldRoot)
    {
        var relativeParts = virtualPath.TrimStart('/').Split('/');
        return Path.Combine(worldRoot, Path.Combine(relativeParts));
    }
}

/// <summary>
/// Print working directory command.
/// </summary>
public class PwdCommand : WizardCommandBase
{
    public override string Name => "pwd";
    public override string Usage => "pwd";
    public override string Description => "Print current working directory";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var cwd = WizardFilesystem.GetWorkingDir(context.Session.SessionId);
        context.Output(cwd);
        return Task.CompletedTask;
    }
}

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
