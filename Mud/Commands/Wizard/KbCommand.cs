using System.Text.Json;
using JitRealm.Mud.AI;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Manage the world knowledge base (KB) - shared facts accessible to all NPCs.
/// </summary>
public class KbCommand : WizardCommandBase
{
    public override string Name => "kb";
    public override IReadOnlyList<string> Aliases => new[] { "knowledge" };
    public override string Usage => "kb <get|set|search|delete> ...";
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
        context.Output("  kb get <key>                    - Get entry by key");
        context.Output("  kb set <key> <json> [tags...]   - Set/update entry");
        context.Output("  kb search <tag1> [tag2...]      - Search by tags");
        context.Output("  kb delete <key>                 - Delete entry");
        context.Output("");
        context.Output("Examples:");
        context.Output("  kb set village:millbrook {\"population\":127} village location");
        context.Output("  kb get village:millbrook");
        context.Output("  kb search village");
        context.Output("  kb delete village:millbrook");
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
        context.Output($"  Updated: {entry.UpdatedAt:u}");
    }

    private static async Task HandleSet(CommandContext context, string[] args, NpcMemorySystem memorySystem)
    {
        if (args.Length < 3)
        {
            context.Output("Usage: kb set <key> <json> [tags...]");
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

        // Remaining args are tags
        var tags = args.Length > 3
            ? args.Skip(3).Select(t => t.ToLowerInvariant()).ToArray()
            : Array.Empty<string>();

        var entry = new WorldKbEntry(
            Key: key,
            Value: json,
            Tags: tags,
            Visibility: "public",
            UpdatedAt: DateTimeOffset.UtcNow);

        await memorySystem.WorldKnowledge.UpsertAsync(entry);

        context.Output($"Set KB entry: {key}");
        context.Output($"  Value: {json.RootElement}");
        if (tags.Length > 0)
            context.Output($"  Tags: [{string.Join(", ", tags)}]");
    }

    private static async Task HandleSearch(CommandContext context, string[] args, NpcMemorySystem memorySystem)
    {
        if (args.Length < 2)
        {
            context.Output("Usage: kb search <tag1> [tag2...]");
            return;
        }

        var tags = args.Skip(1).Select(t => t.ToLowerInvariant()).ToArray();
        var entries = await memorySystem.WorldKnowledge.SearchByTagsAsync(tags, topK: 20);

        context.Output($"=== KB Search: [{string.Join(", ", tags)}] ===");

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
}
