using System.Text.RegularExpressions;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to scaffold new world objects from templates.
/// </summary>
public class CreateCommand : WizardCommandBase
{
    public override string Name => "create";
    public override string[] Aliases => new[] { "scaffold", "new" };
    public override string Usage => "create <type> <name> [variant]";
    public override string Description => "Create a new world object from template";

    private static readonly Dictionary<string, (string Template, string Directory)> TypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["room"] = ("room.template", "Rooms"),
        ["npc"] = ("npc.template", "npcs"),
        ["monster"] = ("monster.template", "npcs"),
        ["item"] = ("item.template", "Items"),
        ["weapon"] = ("weapon.template", "Items"),
        ["armor"] = ("armor.template", "Items"),
    };

    private static readonly Dictionary<string, string> VariantMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Room variants
        ["outdoor"] = "outdoor_room.template",
        ["indoor"] = "room.template",

        // NPC variants
        ["llm"] = "llm_npc.template",
        ["ai"] = "llm_npc.template",

        // Monster variants (llm)
        ["llm_monster"] = "llm_monster.template",
    };

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage(context);
            return;
        }

        var objectType = args[0].ToLowerInvariant();
        var objectName = args[1];
        var variant = args.Length > 2 ? args[2].ToLowerInvariant() : null;

        // Validate object type
        if (!TypeMappings.TryGetValue(objectType, out var mapping))
        {
            context.Output($"Unknown type: {objectType}");
            context.Output("Valid types: room, npc, monster, item, weapon, armor");
            return;
        }

        // Determine template file
        var templateFile = mapping.Template;

        // Handle variants
        if (variant is not null)
        {
            if (objectType == "room" && VariantMappings.TryGetValue(variant, out var roomVariant))
            {
                templateFile = roomVariant;
            }
            else if ((objectType == "npc" || objectType == "monster") && (variant == "llm" || variant == "ai"))
            {
                templateFile = objectType == "monster" ? "llm_monster.template" : "llm_npc.template";
            }
            else
            {
                context.Output($"Unknown variant '{variant}' for type '{objectType}'");
                return;
            }
        }

        // Get paths
        var worldRoot = WizardFilesystem.GetWorldRoot(context);
        var templatePath = Path.Combine(worldRoot, "templates", templateFile);

        // Check template exists
        if (!File.Exists(templatePath))
        {
            context.Output($"Template not found: templates/{templateFile}");
            return;
        }

        // Determine output path based on name argument
        // Path resolution:
        //   /dungeon/cell1  -> World/dungeon/cell1.cs (absolute from World/)
        //   dungeon/cell1   -> World/{cwd}/dungeon/cell1.cs (relative to cwd)
        //   cell1           -> World/{cwd}/cell1.cs (in cwd, or type default if at root)
        var cwd = WizardFilesystem.GetWorkingDir(context.Session.SessionId);

        // Get just the filename part for class name (e.g., "dungeon/cell1" -> "cell1")
        var baseName = objectName.Contains('/') ? objectName[(objectName.LastIndexOf('/') + 1)..] : objectName;
        var className = ToPascalCase(baseName);

        string outputPath;
        string relativePath;

        if (objectName.StartsWith("/"))
        {
            // Absolute path from World root
            var absolutePath = objectName.TrimStart('/');
            relativePath = absolutePath + ".cs";
            outputPath = Path.Combine(worldRoot, absolutePath.Replace('/', Path.DirectorySeparatorChar) + ".cs");
        }
        else if (objectName.Contains('/'))
        {
            // Relative path with subdirectories
            var baseDir = cwd == "/" ? "" : cwd.TrimStart('/') + "/";
            relativePath = baseDir + objectName + ".cs";
            outputPath = Path.Combine(worldRoot, (baseDir + objectName).Replace('/', Path.DirectorySeparatorChar) + ".cs");
        }
        else
        {
            // Simple name - use cwd or type's default directory if at root
            var baseDir = cwd == "/" ? mapping.Directory : cwd.TrimStart('/');
            relativePath = $"{baseDir}/{objectName}.cs";
            outputPath = Path.Combine(worldRoot, baseDir.Replace('/', Path.DirectorySeparatorChar), objectName + ".cs");
        }

        var outputDir = Path.GetDirectoryName(outputPath);

        // Check if file already exists
        if (File.Exists(outputPath))
        {
            context.Output($"File already exists: {relativePath}");
            context.Output("Use a different name or delete the existing file first.");
            return;
        }

        // Ensure output directory exists
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Read template
        var template = await File.ReadAllTextAsync(templatePath);

        // Replace placeholders (use baseName for the display name, not full path)
        var content = template
            .Replace("{{NAME}}", baseName.ToLowerInvariant())
            .Replace("{{CLASS_NAME}}", className)
            .Replace("{{DESCRIPTION}}", $"A {baseName.ToLowerInvariant()}.");

        // Write file
        await File.WriteAllTextAsync(outputPath, content);

        context.Output($"Created: {relativePath}");
        context.Output($"Class: {className}");
        context.Output($"Template: {templateFile}");
        context.Output("");
        context.Output($"Edit with: edit /{relativePath}");
    }

    private void ShowUsage(CommandContext context)
    {
        context.Output("Usage: create <type> <name> [variant]");
        context.Output("");
        context.Output("Types:");
        context.Output("  room <name> [outdoor]     - Create a room (indoor or outdoor)");
        context.Output("  npc <name> [llm]          - Create an NPC (simple or LLM-powered)");
        context.Output("  monster <name> [llm]      - Create a monster (simple or LLM-powered)");
        context.Output("  item <name>               - Create a basic stackable item");
        context.Output("  weapon <name>             - Create an equippable weapon");
        context.Output("  armor <name>              - Create an equippable armor piece");
        context.Output("");
        context.Output("Name can include path:");
        context.Output("  name           - Creates in cwd (or type default if at root)");
        context.Output("  dir/name       - Creates in cwd/dir/ (relative path)");
        context.Output("  /dir/name      - Creates in World/dir/ (absolute path)");
        context.Output("");
        context.Output("Examples:");
        context.Output("  create room tavern              - Create Rooms/tavern.cs (at root)");
        context.Output("  create room meadow outdoor      - Create outdoor Rooms/meadow.cs");
        context.Output("  create room dungeon/cell1       - Create dungeon/cell1.cs in cwd");
        context.Output("  create room /Rooms/special/boss - Create Rooms/special/boss.cs");
        context.Output("  create npc shopkeeper llm       - Create LLM NPC npcs/shopkeeper.cs");
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Split on underscores and spaces
        var parts = Regex.Split(name, @"[\s_]+");

        // Capitalize first letter of each part
        var result = string.Concat(parts.Select(p =>
            string.IsNullOrEmpty(p) ? "" : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));

        return result;
    }
}
