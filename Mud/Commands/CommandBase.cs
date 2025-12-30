namespace JitRealm.Mud.Commands;

/// <summary>
/// Abstract base class for commands providing common functionality.
/// </summary>
public abstract class CommandBase : ICommand
{
    public abstract string Name { get; }

    public virtual IReadOnlyList<string> Aliases => Array.Empty<string>();

    public abstract string Usage { get; }

    public abstract string Description { get; }

    public abstract string Category { get; }

    public virtual bool IsWizardOnly => false;

    public abstract Task ExecuteAsync(CommandContext context, string[] args);

    /// <summary>
    /// Helper to require a minimum number of arguments.
    /// </summary>
    protected bool RequireArgs(CommandContext context, string[] args, int minCount, string? customUsage = null)
    {
        if (args.Length >= minCount) return true;

        context.Output($"Usage: {customUsage ?? Usage}");
        return false;
    }

    /// <summary>
    /// Join arguments starting from an index into a single string.
    /// </summary>
    protected static string JoinArgs(string[] args, int startIndex = 0)
    {
        if (startIndex >= args.Length) return "";
        return string.Join(" ", args.Skip(startIndex));
    }
}
