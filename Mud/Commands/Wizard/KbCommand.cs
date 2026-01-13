using System.Text.Json;
using JitRealm.Mud.AI;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Manage the world knowledge base (KB) - shared facts accessible to NPCs.
/// Supports semantic search and NPC-specific knowledge filtering.
/// </summary>
public class KbCommand : WizardCommandBase
{
    public override string Name => "kb";
    public override IReadOnlyList<string> Aliases => new[] { "knowledge" };
    public override string Usage => "kb <get|set|search|delete|list> ...";
    public override string Description => "Manage world knowledge base";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp(context);
            return;
        }

        var memorySystem = context.State.MemorySystem;
        if (memorySystem is null)
        {
            context.Output("Memory system is not available. Check that Postgres is configured.");
            return;
        }

        var subCommand = args[0].ToLowerInvariant();

        switch (subCommand)
        {
            case "get":
                await HandleGet(context, args, memorySystem);
                break;
            case "set":
                await HandleSet(context, args, memorySystem);
                break;
            case "search":
                await HandleSearch(context, args, memorySystem);
                break;
            case "delete":
            case "del":
                await HandleDelete(context, args, memorySystem);
                break;
            case "list":
                await HandleList(context, args, memorySystem);
                break;
            default:
                context.Output($"Unknown subcommand: {subCommand}");
                ShowHelp(context);
                break;
        }
    }

    private static void ShowHelp(CommandContext context)
    {
        context.Output("Usage: kb <subcommand> [args]");
        context.Output("");
        context.Output("Subcommands:");
        context.Output("  kb get <key>                              - Get entry by key");
        context.Output("  kb set <key> <json> [options] [tags...]   - Set/update entry");
        context.Output("     Options:");
        context.Output("       --npcs npc1,npc2    Restrict to specific NPCs (comma-separated)");
        context.Output("       --summary \"text\"    Text summary for embedding (optional)");
        context.Output("  kb search <query> [options]               - Search with semantic + filters");
        context.Output("     Options:");
        context.Output("       --npc <npcid>       Filter by NPC (includes common + NPC-specific)");
        context.Output("       --tags tag1,tag2    Filter by tags");
        context.Output("       --limit <N>         Max results (default 10)");
        context.Output("  kb delete <key>                           - Delete entry");
        context.Output("  kb list [--limit N]                       - List all entries");
        context.Output("");
        context.Output("Examples:");
        context.Output("  kb set village:millbrook {\"population\":127} millbrook village location");
        context.Output("  kb set secret:shopkeeper:safe {\"combo\":\"1234\"} --npcs npcs/shopkeeper.cs");
        context.Output("  kb search \"directions to blacksmith\" --tags millbrook");
        context.Output("  kb search \"shop prices\" --npc npcs/shopkeeper.cs#000001");
    }

    private static async Task HandleGet(CommandContext context, string[] args, NpcMemorySystem memorySystem)
    {
        if (args.Length < 2)
        {
            context.Output("Usage: kb get <key>");
            return;
        }

        var key = args[1];
        var entry = await memorySystem.WorldKnowledge.GetAsync(key);

        if (entry is null)
        {
            context.Output($"No entry found for key: {key}");
            return;
        }

        context.Output($"=== KB Entry: {entry.Key} ===");
        context.Output($"  Value: {entry.Value.RootElement}");
        context.Output($"  Tags: [{string.Join(", ", entry.Tags)}]");
        context.Output($"  Visibility: {entry.Visibility}");
        if (entry.NpcIds is { Count: > 0 })
            context.Output($"  NPCs: [{string.Join(", ", entry.NpcIds)}]");
        else
            context.Output($"  NPCs: (common knowledge - all NPCs)");
        if (!string.IsNullOrWhiteSpace(entry.Summary))
            context.Output($"  Summary: {entry.Summary}");
        context.Output($"  Updated: {entry.UpdatedAt:u}");
    }

    private static async Task HandleSet(CommandContext context, string[] args, NpcMemorySystem memorySystem)
    {
        if (args.Length < 3)
        {
            context.Output("Usage: kb set <key> <json> [--npcs npc1,npc2] [--summary \"text\"] [tags...]");
            context.Output("  Example: kb set npc:tom:home {\"room\":\"farm\"} npc location tom");
            return;
        }

        var key = args[1];
        var jsonStr = args[2];

        // Validate JSON
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(jsonStr);
        }
        catch (JsonException ex)
        {
            context.Output($"Invalid JSON: {ex.Message}");
            context.Output("  Tip: Use simple JSON like {\"key\":\"value\"} or {\"count\":42}");
            return;
        }

        // Parse options and remaining tags
        string[]? npcIds = null;
        string? summary = null;
        var tags = new List<string>();

        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--npcs" && i + 1 < args.Length)
            {
                npcIds = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
            else if (args[i] == "--summary" && i + 1 < args.Length)
            {
                summary = args[++i];
            }
            else if (!args[i].StartsWith("--"))
            {
                tags.Add(args[i].ToLowerInvariant());
            }
        }

        var visibility = npcIds is { Length: > 0 } ? "npc" : "public";

        var entry = new WorldKbEntry(
            Key: key,
            Value: json,
            Tags: tags,
            Visibility: visibility,
            UpdatedAt: DateTimeOffset.UtcNow,
            NpcIds: npcIds,
            Summary: summary);

        await memorySystem.WorldKnowledge.UpsertAsync(entry);

        context.Output($"Set KB entry: {key}");
        context.Output($"  Value: {json.RootElement}");
        if (tags.Count > 0)
            context.Output($"  Tags: [{string.Join(", ", tags)}]");
        if (npcIds is { Length: > 0 })
            context.Output($"  NPCs: [{string.Join(", ", npcIds)}]");
        if (!string.IsNullOrWhiteSpace(summary))
            context.Output($"  Summary: {summary}");
    }

    private static async Task HandleSearch(CommandContext context, string[] args, NpcMemorySystem memorySystem)
    {
        if (args.Length < 2)
        {
            context.Output("Usage: kb search <query> [--npc <npcid>] [--tags tag1,tag2] [--limit N]");
            return;
        }

        var query = args[1];
        string? npcId = null;
        string[]? tags = null;
        int limit = 10;

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--npc" && i + 1 < args.Length)
            {
                npcId = args[++i];
            }
            else if (args[i] == "--tags" && i + 1 < args.Length)
            {
                tags = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
            else if (args[i] == "--limit" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out limit);
            }
        }

        var searchQuery = new WorldKbSearchQuery(
            QueryText: query,
            Tags: tags,
            NpcId: npcId,
            TopK: limit);

        var entries = await memorySystem.WorldKnowledge.SearchAsync(searchQuery);

        context.Output($"=== KB Search: \"{query}\" ===");
        if (npcId is not null)
            context.Output($"  NPC filter: {npcId}");
        if (tags is { Length: > 0 })
            context.Output($"  Tag filter: [{string.Join(", ", tags)}]");

        if (entries.Count == 0)
        {
            context.Output("  No entries found");
            return;
        }

        foreach (var entry in entries)
        {
            context.Output($"\n  {entry.Key}");
            context.Output($"    Value: {entry.Value.RootElement}");
            context.Output($"    Tags: [{string.Join(", ", entry.Tags)}]");
            if (entry.NpcIds is { Count: > 0 })
                context.Output($"    NPCs: [{string.Join(", ", entry.NpcIds)}] (restricted)");
        }

        context.Output($"\n({entries.Count} entries found)");
    }

    private static async Task HandleDelete(CommandContext context, string[] args, NpcMemorySystem memorySystem)
    {
        if (args.Length < 2)
        {
            context.Output("Usage: kb delete <key>");
            return;
        }

        var key = args[1];

        // Check if entry exists first
        var existing = await memorySystem.WorldKnowledge.GetAsync(key);
        if (existing is null)
        {
            context.Output($"No entry found for key: {key}");
            return;
        }

        await memorySystem.WorldKnowledge.DeleteAsync(key);
        context.Output($"Deleted KB entry: {key}");
    }

    private static async Task HandleList(CommandContext context, string[] args, NpcMemorySystem memorySystem)
    {
        int limit = 20;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--limit" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out limit);
            }
        }

        // Search with no filters to get all common knowledge entries
        var entries = await memorySystem.WorldKnowledge.SearchAsync(new WorldKbSearchQuery(
            QueryText: null,
            Tags: null,
            NpcId: null,
            TopK: limit));

        context.Output($"=== KB Entries (common knowledge, up to {limit}) ===");

        if (entries.Count == 0)
        {
            context.Output("  No entries found");
            return;
        }

        foreach (var entry in entries)
        {
            var tagList = entry.Tags.Count > 0 ? $" [{string.Join(",", entry.Tags)}]" : "";
            context.Output($"  {entry.Key}{tagList}");
        }

        context.Output($"\n({entries.Count} entries shown)");
    }
}
