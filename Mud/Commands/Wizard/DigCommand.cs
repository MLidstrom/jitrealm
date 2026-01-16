using System.Text.RegularExpressions;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to create a new room and link it to the current room.
/// Combines room creation with exit linking in one step.
/// </summary>
public class DigCommand : WizardCommandBase
{
    public override string Name => "dig";
    public override string[] Aliases => new[] { "excavate" };
    public override string Usage => "dig <direction> <room-name> [outdoor] [--oneway]";
    public override string Description => "Create a new room and link it to the current room";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage(context);
            return;
        }

        // Parse arguments
        var direction = args[0];
        var roomName = args[1];
        var isOutdoor = args.Any(a => a.Equals("outdoor", StringComparison.OrdinalIgnoreCase));
        var isOneWay = args.Any(a => a.Equals("--oneway", StringComparison.OrdinalIgnoreCase));

        // Validate direction
        var normalizedDir = DirectionHelper.NormalizeDirection(direction);
        if (!DirectionHelper.IsValidDirection(normalizedDir))
        {
            context.Output($"Unknown direction: {direction}");
            context.Output($"Valid directions: {DirectionHelper.GetValidDirectionsList()}");
            return;
        }

        // Get current room
        var currentRoomId = context.GetPlayerLocation();
        if (currentRoomId is null)
        {
            context.Output("You are not in a room.");
            return;
        }

        // Get current room object to check existing exits
        var currentRoom = context.State.Objects!.Get<IRoom>(currentRoomId);
        if (currentRoom is null)
        {
            context.Output("Cannot access current room.");
            return;
        }

        // Check if exit already exists
        if (currentRoom.Exits.ContainsKey(normalizedDir))
        {
            context.Output($"Exit '{normalizedDir}' already exists in this room.");
            context.Output("Use 'unlink' to remove it first, or choose a different direction.");
            return;
        }

        // Get world root and paths
        var worldRoot = WizardFilesystem.GetWorldRoot(context);
        var currentBlueprintId = GetBlueprintIdFromInstanceId(currentRoomId);
        var currentFilePath = RoomFileEditor.GetFilePathFromBlueprintId(currentBlueprintId, worldRoot);

        if (!File.Exists(currentFilePath))
        {
            context.Output($"Cannot modify current room: file not found at {currentFilePath}");
            context.Output("This may be a generated or non-file-based room.");
            return;
        }

        // Determine new room blueprint ID and file path
        // Path resolution:
        //   /dungeon/cell1  -> World/dungeon/cell1.cs (absolute from World/)
        //   dungeon/cell1   -> World/{cwd}/dungeon/cell1.cs (relative to cwd)
        //   cell1           -> World/{cwd}/cell1.cs (in cwd, or Rooms/ if at root)
        var cwd = WizardFilesystem.GetWorkingDir(context.Session.SessionId);

        string newBlueprintId;
        string newFilePath;

        if (roomName.StartsWith("/"))
        {
            // Absolute path from World root
            var absolutePath = roomName.TrimStart('/');
            newBlueprintId = absolutePath + ".cs";
            newFilePath = Path.Combine(worldRoot, absolutePath.Replace('/', Path.DirectorySeparatorChar) + ".cs");
        }
        else if (roomName.Contains('/'))
        {
            // Relative path with subdirectories
            var baseDir = cwd == "/" ? "" : cwd.TrimStart('/') + "/";
            newBlueprintId = baseDir + roomName + ".cs";
            newFilePath = Path.Combine(worldRoot, (baseDir + roomName).Replace('/', Path.DirectorySeparatorChar) + ".cs");
        }
        else
        {
            // Simple name - use cwd or Rooms/ if at root
            var baseDir = cwd == "/" ? "Rooms" : cwd.TrimStart('/');
            newBlueprintId = $"{baseDir}/{roomName}.cs";
            newFilePath = Path.Combine(worldRoot, baseDir.Replace('/', Path.DirectorySeparatorChar), roomName + ".cs");
        }

        // Check if new room file already exists
        if (File.Exists(newFilePath))
        {
            context.Output($"{newBlueprintId} already exists.");
            context.Output($"Use 'link {normalizedDir} {newBlueprintId}' to connect to it instead.");
            return;
        }

        // Determine template
        var templateFile = isOutdoor ? "outdoor_room.template" : "room.template";
        var templatePath = Path.Combine(worldRoot, "templates", templateFile);

        if (!File.Exists(templatePath))
        {
            context.Output($"Template not found: templates/{templateFile}");
            return;
        }

        // Create directory for new room if needed
        var newRoomDir = Path.GetDirectoryName(newFilePath);
        if (newRoomDir is not null && !Directory.Exists(newRoomDir))
        {
            Directory.CreateDirectory(newRoomDir);
        }

        // Read template and generate new room content
        var template = await File.ReadAllTextAsync(templatePath);
        // Get just the filename part for class name (e.g., "dungeon/cell1" -> "cell1")
        var baseName = roomName.Contains('/') ? roomName[(roomName.LastIndexOf('/') + 1)..] : roomName;
        var className = ToPascalCase(baseName);
        var displayName = FormatDisplayName(baseName);

        var newRoomContent = template
            .Replace("{{NAME}}", displayName)
            .Replace("{{CLASS_NAME}}", className)
            .Replace("{{DESCRIPTION}}", $"A {displayName.ToLowerInvariant()}.");

        // Add reverse exit to the new room (unless --oneway)
        if (!isOneWay)
        {
            var reverseDir = DirectionHelper.GetReverseDirection(normalizedDir);
            if (reverseDir is not null)
            {
                // Replace the empty Exits dictionary with one containing the reverse exit
                newRoomContent = AddExitToTemplate(newRoomContent, reverseDir, currentBlueprintId);
            }
        }

        // Write new room file
        await File.WriteAllTextAsync(newFilePath, newRoomContent);
        context.Output($"Created: {newBlueprintId} (class: {className})");

        // Add exit to current room
        var result = await RoomFileEditor.AddExitAsync(currentFilePath, normalizedDir, newBlueprintId);
        if (!result.Success)
        {
            context.Output($"Warning: Could not add exit to current room: {result.ErrorMessage}");
            context.Output("You may need to add the exit manually.");
        }
        else
        {
            context.Output($"Added exit '{normalizedDir}' to current room → {newBlueprintId}");
        }

        // Report reverse exit status
        if (!isOneWay)
        {
            var reverseDir = DirectionHelper.GetReverseDirection(normalizedDir);
            if (reverseDir is not null)
            {
                context.Output($"Added exit '{reverseDir}' to {newBlueprintId} → {currentBlueprintId}");
            }
            else
            {
                context.Output($"Note: No reverse direction for '{normalizedDir}'. New room has no exit back.");
            }
        }
        else
        {
            context.Output($"(one-way exit - no reverse link created)");
        }

        // Reload current room
        context.Output("Reloading current room...");
        try
        {
            await context.State.Objects!.ReloadBlueprintAsync(currentBlueprintId, context.State);
        }
        catch (Exception ex)
        {
            context.Output($"Warning: Reload failed: {ex.Message}");
            context.Output("You may need to manually reload with 'reload here'.");
        }

        context.Output("Done.");
        context.Output($"Use 'go {normalizedDir}' to visit the new room.");
    }

    private void ShowUsage(CommandContext context)
    {
        context.Output("Usage: dig <direction> <room-name> [outdoor] [--oneway]");
        context.Output("");
        context.Output("Creates a new room and links it to the current room.");
        context.Output("By default, creates bidirectional exits (both directions).");
        context.Output("");
        context.Output("Options:");
        context.Output("  outdoor    Create an outdoor room (shows time/weather)");
        context.Output("  --oneway   Only create exit from current room (no reverse)");
        context.Output("");
        context.Output("Examples:");
        context.Output("  dig north tavern           - Create Rooms/tavern.cs with south exit back");
        context.Output("  dig east dungeon/cell1     - Create Rooms/dungeon/cell1.cs (subdirectory)");
        context.Output("  dig up tower outdoor       - Create outdoor room");
        context.Output("  dig down basement --oneway - One-way exit (no way back)");
        context.Output("");
        context.Output("Use 'link' to connect to existing rooms instead.");
    }

    /// <summary>
    /// Extract blueprint ID from instance ID (removes #NNNNNN suffix).
    /// </summary>
    private static string GetBlueprintIdFromInstanceId(string instanceId)
    {
        var hashIndex = instanceId.IndexOf('#');
        return hashIndex > 0 ? instanceId[..hashIndex] : instanceId;
    }

    /// <summary>
    /// Convert a name to PascalCase for class names.
    /// </summary>
    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Split on underscores, spaces, and hyphens
        var parts = Regex.Split(name, @"[\s_\-]+");

        // Capitalize first letter of each part
        var result = string.Concat(parts.Select(p =>
            string.IsNullOrEmpty(p) ? "" : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));

        return result;
    }

    /// <summary>
    /// Format a name for display (spaces instead of underscores, title case).
    /// </summary>
    private static string FormatDisplayName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Replace underscores and hyphens with spaces
        var spaced = Regex.Replace(name, @"[_\-]+", " ");

        // Title case
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }

    /// <summary>
    /// Add an exit to a room template content.
    /// Replaces the empty/commented Exits dictionary with one containing the exit.
    /// </summary>
    private static string AddExitToTemplate(string content, string direction, string targetRoom)
    {
        // Find the Exits property and replace the commented-out content with actual exit
        var pattern = @"(public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>\s*\{)[^}]*(\})";

        var replacement = $"$1\n        [\"{direction}\"] = \"{targetRoom}\",\n    $2";

        return Regex.Replace(content, pattern, replacement, RegexOptions.Singleline);
    }
}
