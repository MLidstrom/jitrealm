using System.Text.Json;
using System.Text.RegularExpressions;

namespace JitRealm.Mud.AI;

/// <summary>
/// Seeds the world knowledge base from a seed file.
/// Parses kb set commands and executes them against the WorldKnowledge store.
/// </summary>
public static class KbSeeder
{
    /// <summary>
    /// Load and execute KB seed commands from a file.
    /// </summary>
    /// <param name="seedFilePath">Path to the seed file.</param>
    /// <param name="worldKnowledge">The world knowledge base to populate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of entries added.</returns>
    public static async Task<int> SeedFromFileAsync(
        string seedFilePath,
        IWorldKnowledgeBase worldKnowledge,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(seedFilePath))
        {
            Console.WriteLine($"[KB Seeder] Seed file not found: {seedFilePath}");
            return 0;
        }

        var lines = await File.ReadAllLinesAsync(seedFilePath, cancellationToken);
        var count = 0;
        var errors = 0;

        Console.WriteLine($"[KB Seeder] Loading seed file: {seedFilePath}");
        Console.WriteLine($"[KB Seeder] Processing {lines.Length} lines...");

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            // Parse and execute kb set command
            if (line.StartsWith("kb set ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var entry = ParseKbSetCommand(line);
                    if (entry is not null)
                    {
                        await worldKnowledge.UpsertAsync(entry, cancellationToken);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[KB Seeder] Error processing line: {line}");
                    Console.WriteLine($"[KB Seeder]   {ex.Message}");
                    errors++;
                }
            }
        }

        Console.WriteLine($"[KB Seeder] Complete: {count} entries added, {errors} errors");
        return count;
    }

    /// <summary>
    /// Parse a "kb set key {json} [--npcs ...] [--summary ...] [tags...]" command.
    /// </summary>
    private static WorldKbEntry? ParseKbSetCommand(string line)
    {
        // Remove "kb set " prefix
        var content = line.Substring("kb set ".Length).Trim();

        if (string.IsNullOrEmpty(content))
            return null;

        // Parse key (everything before the first {)
        var jsonStart = content.IndexOf('{');
        if (jsonStart < 0)
        {
            Console.WriteLine($"[KB Seeder] No JSON found in: {line}");
            return null;
        }

        var key = content.Substring(0, jsonStart).Trim();
        if (string.IsNullOrEmpty(key))
        {
            Console.WriteLine($"[KB Seeder] No key found in: {line}");
            return null;
        }

        // Find matching closing brace for the JSON
        var braceCount = 0;
        var jsonEnd = -1;
        for (int i = jsonStart; i < content.Length; i++)
        {
            if (content[i] == '{') braceCount++;
            else if (content[i] == '}') braceCount--;

            if (braceCount == 0)
            {
                jsonEnd = i;
                break;
            }
        }

        if (jsonEnd < 0)
        {
            Console.WriteLine($"[KB Seeder] Unbalanced braces in: {line}");
            return null;
        }

        var jsonStr = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(jsonStr);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[KB Seeder] Invalid JSON in: {line}");
            Console.WriteLine($"[KB Seeder]   {ex.Message}");
            return null;
        }

        // Parse remaining: [--npcs ...] [--summary "..."] [tags...]
        var remainder = content.Substring(jsonEnd + 1).Trim();

        string[]? npcIds = null;
        string? summary = null;
        var tags = new List<string>();

        // Use regex to find --summary "..." (quoted text)
        var summaryMatch = Regex.Match(remainder, @"--summary\s+""([^""]+)""");
        if (summaryMatch.Success)
        {
            summary = summaryMatch.Groups[1].Value;
            remainder = remainder.Remove(summaryMatch.Index, summaryMatch.Length).Trim();
        }

        // Parse remaining tokens
        var tokens = TokenizeRemainder(remainder);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] == "--npcs" && i + 1 < tokens.Count)
            {
                npcIds = tokens[++i].Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
            else if (!tokens[i].StartsWith("--"))
            {
                tags.Add(tokens[i].ToLowerInvariant());
            }
        }

        var visibility = npcIds is { Length: > 0 } ? "npc" : "public";

        return new WorldKbEntry(
            Key: key,
            Value: json,
            Tags: tags,
            Visibility: visibility,
            UpdatedAt: DateTimeOffset.UtcNow,
            NpcIds: npcIds,
            Summary: summary);
    }

    /// <summary>
    /// Tokenize a string, respecting quoted strings.
    /// </summary>
    private static List<string> TokenizeRemainder(string input)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in input)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }
}
