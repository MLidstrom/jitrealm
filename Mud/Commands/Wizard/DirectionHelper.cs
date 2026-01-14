namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Shared utility for direction validation and reverse mapping.
/// Used by dig, link, and unlink commands.
/// </summary>
public static class DirectionHelper
{
    /// <summary>
    /// Mapping of directions to their opposites.
    /// </summary>
    private static readonly Dictionary<string, string> ReverseDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["north"] = "south",
        ["south"] = "north",
        ["east"] = "west",
        ["west"] = "east",
        ["up"] = "down",
        ["down"] = "up",
        ["northeast"] = "southwest",
        ["southwest"] = "northeast",
        ["northwest"] = "southeast",
        ["southeast"] = "northwest",
        ["ne"] = "sw",
        ["sw"] = "ne",
        ["nw"] = "se",
        ["se"] = "nw",
        ["in"] = "out",
        ["out"] = "in",
        ["enter"] = "exit",
        ["exit"] = "enter"
    };

    /// <summary>
    /// Canonical short direction aliases.
    /// </summary>
    private static readonly Dictionary<string, string> DirectionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["n"] = "north",
        ["s"] = "south",
        ["e"] = "east",
        ["w"] = "west",
        ["u"] = "up",
        ["d"] = "down",
        ["ne"] = "northeast",
        ["nw"] = "northwest",
        ["se"] = "southeast",
        ["sw"] = "southwest"
    };

    /// <summary>
    /// Common valid directions.
    /// </summary>
    public static readonly IReadOnlyList<string> CommonDirections = new[]
    {
        "north", "south", "east", "west", "up", "down",
        "northeast", "northwest", "southeast", "southwest",
        "in", "out", "enter", "exit"
    };

    /// <summary>
    /// Check if a direction is valid (has a known reverse).
    /// </summary>
    public static bool IsValidDirection(string direction)
    {
        var normalized = NormalizeDirection(direction);
        return ReverseDirections.ContainsKey(normalized);
    }

    /// <summary>
    /// Get the reverse direction for a given direction.
    /// Returns null if no reverse is known.
    /// </summary>
    public static string? GetReverseDirection(string direction)
    {
        var normalized = NormalizeDirection(direction);
        return ReverseDirections.TryGetValue(normalized, out var reverse) ? reverse : null;
    }

    /// <summary>
    /// Normalize a direction (expand aliases, lowercase).
    /// </summary>
    public static string NormalizeDirection(string direction)
    {
        var lower = direction.ToLowerInvariant();
        return DirectionAliases.TryGetValue(lower, out var expanded) ? expanded : lower;
    }

    /// <summary>
    /// Check if a direction has a known reverse.
    /// </summary>
    public static bool HasReverseDirection(string direction)
    {
        return GetReverseDirection(direction) is not null;
    }

    /// <summary>
    /// Get a formatted list of valid directions for error messages.
    /// </summary>
    public static string GetValidDirectionsList()
    {
        return string.Join(", ", CommonDirections.Take(6)) + " (and more)";
    }
}
