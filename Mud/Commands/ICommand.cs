namespace JitRealm.Mud.Commands;

/// <summary>
/// Interface for all MUD commands. Commands can be registered with CommandRegistry
/// to enable structured command handling with help, aliases, and usage information.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// The primary name of the command (e.g., "look", "go", "kill").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Alternative names for the command (e.g., "attack" for "kill").
    /// </summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// Usage syntax (e.g., "go <direction>", "kill <target>").
    /// </summary>
    string Usage { get; }

    /// <summary>
    /// Short description for help display.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Category for grouping in help (e.g., "Navigation", "Combat", "Social").
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Whether this command requires wizard privileges.
    /// </summary>
    bool IsWizardOnly { get; }

    /// <summary>
    /// Execute the command.
    /// </summary>
    /// <param name="context">The command execution context.</param>
    /// <param name="args">Arguments passed to the command.</param>
    /// <returns>Task that completes when command is done.</returns>
    Task ExecuteAsync(CommandContext context, string[] args);
}
