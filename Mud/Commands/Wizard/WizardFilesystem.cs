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
