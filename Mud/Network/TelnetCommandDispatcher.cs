using JitRealm.Mud.Commands;

namespace JitRealm.Mud.Network;

/// <summary>
/// Dispatches commands from telnet sessions through the unified CommandRegistry.
/// Eliminates duplicate command implementations between console and telnet modes.
/// </summary>
public sealed class TelnetCommandDispatcher
{
    private readonly WorldState _state;
    private readonly CommandRegistry _commandRegistry;
    private readonly Func<string, MudContext> _contextFactory;

    public TelnetCommandDispatcher(
        WorldState state,
        CommandRegistry commandRegistry,
        Func<string, MudContext> contextFactory)
    {
        _state = state;
        _commandRegistry = commandRegistry;
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// Try to execute a command through the registry.
    /// </summary>
    /// <param name="session">The telnet session</param>
    /// <param name="commandName">The command name</param>
    /// <param name="args">Command arguments</param>
    /// <returns>True if command was found and executed, false if unknown command</returns>
    public async Task<bool> TryExecuteAsync(ISession session, string commandName, string[] args)
    {
        var command = _commandRegistry.GetCommand(commandName);
        if (command is null)
            return false;

        // Check wizard permission
        if (command.IsWizardOnly && !session.IsWizard)
        {
            await session.WriteLineAsync("That command requires wizard privileges.");
            return true; // Command exists but not allowed
        }

        // Create output delegate that writes to session
        var outputQueue = new Queue<string>();
        void Output(string message) => outputQueue.Enqueue(message);

        // Create command context
        var context = new CommandContext
        {
            State = _state,
            PlayerId = session.PlayerId!,
            Session = session,
            Output = Output,
            CreateContext = _contextFactory,
            RawInput = string.Join(" ", new[] { commandName }.Concat(args))
        };

        // Execute the command
        await command.ExecuteAsync(context, args);

        // Flush queued output to session
        foreach (var line in outputQueue)
        {
            await session.WriteLineAsync(line);
        }

        return true;
    }

    /// <summary>
    /// Get a command by name for inspection (e.g., for help).
    /// </summary>
    public ICommand? GetCommand(string name) => _commandRegistry.GetCommand(name);

    /// <summary>
    /// Get all registered commands.
    /// </summary>
    public IEnumerable<ICommand> GetAllCommands() => _commandRegistry.GetAllCommands();
}
