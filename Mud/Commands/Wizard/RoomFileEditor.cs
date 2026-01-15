using System.Text.RegularExpressions;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Shared utility for editing room source files.
/// Used by dig, link, and unlink commands to modify exits.
/// </summary>
public static class RoomFileEditor
{
    /// <summary>
    /// Result of a room file edit operation.
    /// </summary>
    public record EditResult(bool Success, string? ErrorMessage = null);

    /// <summary>
    /// Add an exit to a room's Exits property.
    /// </summary>
    public static async Task<EditResult> AddExitAsync(string roomFilePath, string direction, string targetRoom)
    {
        if (!File.Exists(roomFilePath))
        {
            return new EditResult(false, $"Room file not found: {roomFilePath}");
        }

        var content = await File.ReadAllTextAsync(roomFilePath);

        // Check if exit already exists
        var existingExits = ParseExits(content);
        var normalizedDir = DirectionHelper.NormalizeDirection(direction);

        if (existingExits.ContainsKey(normalizedDir))
        {
            return new EditResult(false, $"Exit '{normalizedDir}' already exists in this room.");
        }

        // Find and modify the Exits property
        var modifiedContent = AddExitToContent(content, normalizedDir, targetRoom);

        if (modifiedContent is null)
        {
            return new EditResult(false, "Could not find Exits property in room file. Is it using the standard pattern?");
        }

        await File.WriteAllTextAsync(roomFilePath, modifiedContent);
        return new EditResult(true);
    }

    /// <summary>
    /// Remove an exit from a room's Exits property.
    /// </summary>
    public static async Task<EditResult> RemoveExitAsync(string roomFilePath, string direction)
    {
        if (!File.Exists(roomFilePath))
        {
            return new EditResult(false, $"Room file not found: {roomFilePath}");
        }

        var content = await File.ReadAllTextAsync(roomFilePath);

        // Check if exit exists
        var existingExits = ParseExits(content);
        var normalizedDir = DirectionHelper.NormalizeDirection(direction);

        if (!existingExits.ContainsKey(normalizedDir))
        {
            return new EditResult(false, $"No exit '{normalizedDir}' in this room.");
        }

        // Find and modify the Exits property
        var modifiedContent = RemoveExitFromContent(content, normalizedDir);

        if (modifiedContent is null)
        {
            return new EditResult(false, "Could not modify Exits property in room file.");
        }

        await File.WriteAllTextAsync(roomFilePath, modifiedContent);
        return new EditResult(true);
    }

    /// <summary>
    /// Check if a room file has a specific exit.
    /// </summary>
    public static bool HasExit(string roomFilePath, string direction)
    {
        if (!File.Exists(roomFilePath))
            return false;

        var content = File.ReadAllText(roomFilePath);
        var exits = ParseExits(content);
        return exits.ContainsKey(DirectionHelper.NormalizeDirection(direction));
    }

    /// <summary>
    /// Get all exits from a room file.
    /// </summary>
    public static Dictionary<string, string> GetExits(string roomFilePath)
    {
        if (!File.Exists(roomFilePath))
            return new Dictionary<string, string>();

        var content = File.ReadAllText(roomFilePath);
        return ParseExits(content);
    }

    /// <summary>
    /// Parse exits from room file content.
    /// Handles both dictionary initializer styles.
    /// </summary>
    private static Dictionary<string, string> ParseExits(string content)
    {
        var exits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Pattern 1: new Dictionary<string, string> { ["dir"] = "target", ... }
        // Pattern 2: new Dictionary<string, string>() { ... } (with parentheses)
        // Pattern 3: new() { ["dir"] = "target", ... }
        // All can span multiple lines, parentheses are optional
        var dictPattern = @"Exits\s*(?:=>|=)\s*new\s*(?:Dictionary<string,\s*string>)?\s*(?:\(\s*\))?\s*\{([^}]*)\}";
        var match = Regex.Match(content, dictPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (match.Success)
        {
            var dictContent = match.Groups[1].Value;

            // Match individual entries: ["key"] = "value"
            var entryPattern = @"\[\s*""([^""]+)""\s*\]\s*=\s*""([^""]+)""";
            var entries = Regex.Matches(dictContent, entryPattern);

            foreach (Match entry in entries)
            {
                var dir = entry.Groups[1].Value;
                var target = entry.Groups[2].Value;
                exits[dir] = target;
            }
        }

        return exits;
    }

    /// <summary>
    /// Add an exit entry to the Exits property content.
    /// </summary>
    private static string? AddExitToContent(string content, string direction, string targetRoom)
    {
        // Find the Exits property with dictionary initializer (parentheses optional)
        var dictPattern = @"(Exits\s*(?:=>|=)\s*new\s*(?:Dictionary<string,\s*string>)?\s*(?:\(\s*\))?\s*\{)([^}]*)(\})";
        var match = Regex.Match(content, dictPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        var prefix = match.Groups[1].Value;
        var existingContent = match.Groups[2].Value;
        var suffix = match.Groups[3].Value;

        // Determine formatting based on existing content
        var newEntry = $@"[""{direction}""] = ""{targetRoom}""";

        string newContent;
        if (string.IsNullOrWhiteSpace(existingContent))
        {
            // Empty dictionary - add with newline formatting
            newContent = $"{prefix}\n        {newEntry}\n    {suffix}";
        }
        else
        {
            // Has existing entries - match indentation and add comma
            var trimmed = existingContent.TrimEnd();
            if (!trimmed.EndsWith(","))
            {
                trimmed += ",";
            }

            // Detect indentation from existing content
            var indentMatch = Regex.Match(existingContent, @"\n(\s+)\[");
            var indent = indentMatch.Success ? indentMatch.Groups[1].Value : "        ";

            newContent = $"{prefix}{trimmed}\n{indent}{newEntry}\n    {suffix}";
        }

        return content.Substring(0, match.Index) + newContent + content.Substring(match.Index + match.Length);
    }

    /// <summary>
    /// Remove an exit entry from the Exits property content.
    /// </summary>
    private static string? RemoveExitFromContent(string content, string direction)
    {
        // Match the specific entry to remove
        // Handle both with and without trailing comma
        var entryPattern = $@",?\s*\[""{Regex.Escape(direction)}""\]\s*=\s*""[^""]+""[,\s]*";

        // First try to find the Exits dictionary (parentheses optional)
        var dictPattern = @"(Exits\s*(?:=>|=)\s*new\s*(?:Dictionary<string,\s*string>)?\s*(?:\(\s*\))?\s*\{)([^}]*)(\})";
        var dictMatch = Regex.Match(content, dictPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (!dictMatch.Success)
            return null;

        var prefix = dictMatch.Groups[1].Value;
        var dictContent = dictMatch.Groups[2].Value;
        var suffix = dictMatch.Groups[3].Value;

        // Remove the entry from the dictionary content
        var modifiedDictContent = Regex.Replace(dictContent, entryPattern, "", RegexOptions.IgnoreCase);

        // Clean up any double commas or leading/trailing commas
        modifiedDictContent = Regex.Replace(modifiedDictContent, @",\s*,", ",");
        modifiedDictContent = Regex.Replace(modifiedDictContent, @"^\s*,", "");
        modifiedDictContent = Regex.Replace(modifiedDictContent, @",\s*$", "");

        var newContent = prefix + modifiedDictContent + suffix;
        return content.Substring(0, dictMatch.Index) + newContent + content.Substring(dictMatch.Index + dictMatch.Length);
    }

    /// <summary>
    /// Get the blueprint ID from a room file path.
    /// E.g., "C:\...\World\Rooms\tavern.cs" -> "Rooms/tavern"
    /// </summary>
    public static string? GetBlueprintIdFromPath(string filePath, string worldRoot)
    {
        try
        {
            var relativePath = Path.GetRelativePath(worldRoot, filePath);
            // Normalize to forward slashes and remove .cs extension
            var blueprintId = relativePath.Replace('\\', '/');
            if (blueprintId.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                blueprintId = blueprintId[..^3];
            }
            return blueprintId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get the file path from a blueprint ID.
    /// E.g., "Rooms/tavern" -> "C:\...\World\Rooms\tavern.cs"
    /// </summary>
    public static string GetFilePathFromBlueprintId(string blueprintId, string worldRoot)
    {
        var path = blueprintId.Replace('/', Path.DirectorySeparatorChar);
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            path += ".cs";
        }
        return Path.Combine(worldRoot, path);
    }
}
