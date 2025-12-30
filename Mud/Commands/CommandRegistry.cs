namespace JitRealm.Mud.Commands;

/// <summary>
/// Registry for all available commands. Provides command lookup by name or alias,
/// categorization, and help functionality.
/// </summary>
public class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ICommand> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ICommand> _allCommands = new();

    /// <summary>
    /// Register a command with the registry.
    /// </summary>
    public void Register(ICommand command)
    {
        _commands[command.Name] = command;
        _allCommands.Add(command);

        foreach (var alias in command.Aliases)
        {
            _aliases[alias] = command;
        }
    }

    /// <summary>
    /// Look up a command by name or alias.
    /// </summary>
    public ICommand? GetCommand(string nameOrAlias)
    {
        if (_commands.TryGetValue(nameOrAlias, out var cmd))
            return cmd;
        if (_aliases.TryGetValue(nameOrAlias, out cmd))
            return cmd;
        return null;
    }

    /// <summary>
    /// Get all registered commands.
    /// </summary>
    public IReadOnlyList<ICommand> GetAllCommands() => _allCommands;

    /// <summary>
    /// Get commands grouped by category.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ICommand>> GetCommandsByCategory()
    {
        return _allCommands
            .GroupBy(c => c.Category)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ICommand>)g.OrderBy(c => c.Name).ToList());
    }

    /// <summary>
    /// Get commands available to a player (filters out wizard commands for non-wizards).
    /// </summary>
    public IEnumerable<ICommand> GetAvailableCommands(bool isWizard)
    {
        return _allCommands.Where(c => !c.IsWizardOnly || isWizard);
    }

    /// <summary>
    /// Get help text for a specific command.
    /// </summary>
    public string? GetCommandHelp(string commandName)
    {
        var cmd = GetCommand(commandName);
        if (cmd is null) return null;

        var help = $"{cmd.Name} - {cmd.Description}\n";
        help += $"Usage: {cmd.Usage}\n";
        help += $"Category: {cmd.Category}";

        if (cmd.Aliases.Count > 0)
        {
            help += $"\nAliases: {string.Join(", ", cmd.Aliases)}";
        }

        if (cmd.IsWizardOnly)
        {
            help += "\n(Wizard only)";
        }

        return help;
    }

    /// <summary>
    /// Get a summary of all available commands grouped by category.
    /// </summary>
    public string GetHelpSummary(bool isWizard)
    {
        var categories = GetCommandsByCategory();
        var lines = new List<string> { "=== Available Commands ===" };

        foreach (var category in categories.Keys.OrderBy(k => k))
        {
            var commands = categories[category]
                .Where(c => !c.IsWizardOnly || isWizard)
                .ToList();

            if (commands.Count == 0) continue;

            lines.Add($"\n{category}:");
            foreach (var cmd in commands)
            {
                var aliasStr = cmd.Aliases.Count > 0
                    ? $" ({string.Join("/", cmd.Aliases)})"
                    : "";
                lines.Add($"  {cmd.Name}{aliasStr} - {cmd.Description}");
            }
        }

        lines.Add("\nType 'help <command>' for detailed usage.");
        return string.Join("\n", lines);
    }
}
