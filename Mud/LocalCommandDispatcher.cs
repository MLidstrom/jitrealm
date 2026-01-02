using JitRealm.Mud.Security;

namespace JitRealm.Mud;

/// <summary>
/// Finds and executes local commands from rooms and items.
/// Local commands are context-sensitive commands provided by objects
/// the player is currently interacting with.
/// </summary>
public class LocalCommandDispatcher
{
    private readonly WorldState _state;
    private readonly TimeSpan _timeout;

    public LocalCommandDispatcher(WorldState state, TimeSpan? timeout = null)
    {
        _state = state;
        _timeout = timeout ?? SecurityPolicy.Default.HookTimeout;
    }

    /// <summary>
    /// Try to find and execute a local command.
    /// Checks room, inventory items, and equipped items in order.
    /// </summary>
    /// <param name="playerId">The player executing the command</param>
    /// <param name="command">The command name (lowercase)</param>
    /// <param name="args">Arguments passed to the command</param>
    /// <param name="createContext">Factory to create IMudContext for an object</param>
    /// <returns>True if command was found and executed</returns>
    public async Task<bool> TryExecuteAsync(
        string playerId,
        string command,
        string[] args,
        Func<string, IMudContext> createContext)
    {
        // Check room first
        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId != null)
        {
            var room = _state.Objects?.Get<IRoom>(roomId);
            if (room is IHasCommands roomWithCommands)
            {
                var match = FindCommand(roomWithCommands.LocalCommands, command);
                if (match != null)
                {
                    var ctx = createContext(roomId);
                    await ExecuteWithTimeoutAsync(roomWithCommands, match.Name, args, playerId, ctx);
                    return true;
                }
            }
        }

        // Check inventory items
        var inventory = _state.Containers.GetContents(playerId);
        foreach (var itemId in inventory)
        {
            var item = _state.Objects?.Get<IItem>(itemId);
            if (item is IHasCommands itemWithCommands)
            {
                var match = FindCommand(itemWithCommands.LocalCommands, command);
                if (match != null)
                {
                    var ctx = createContext(itemId);
                    await ExecuteWithTimeoutAsync(itemWithCommands, match.Name, args, playerId, ctx);
                    return true;
                }
            }
        }

        // Check equipped items (that aren't already in inventory)
        var equipped = _state.Equipment.GetAllEquipped(playerId);
        foreach (var (slot, equippedItemId) in equipped)
        {
            if (inventory.Contains(equippedItemId)) continue; // Already checked

            var equippedItem = _state.Objects?.Get<IItem>(equippedItemId);
            if (equippedItem is IHasCommands equippedWithCommands)
            {
                var match = FindCommand(equippedWithCommands.LocalCommands, command);
                if (match != null)
                {
                    var ctx = createContext(equippedItemId);
                    await ExecuteWithTimeoutAsync(equippedWithCommands, match.Name, args, playerId, ctx);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Get all local commands currently available to a player.
    /// Used by the help command to display context-sensitive commands.
    /// </summary>
    /// <param name="playerId">The player to check commands for</param>
    /// <returns>Tuples of (source name, command info)</returns>
    public IEnumerable<(string Source, LocalCommandInfo Command)> GetAvailableCommands(string playerId)
    {
        // Room commands
        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId != null)
        {
            var room = _state.Objects?.Get<IRoom>(roomId);
            if (room is IHasCommands roomWithCommands)
            {
                foreach (var cmd in roomWithCommands.LocalCommands)
                    yield return (room.Name, cmd);
            }
        }

        // Inventory item commands
        var inventory = _state.Containers.GetContents(playerId);
        foreach (var itemId in inventory)
        {
            var item = _state.Objects?.Get<IItem>(itemId);
            if (item is IHasCommands itemWithCommands)
            {
                foreach (var cmd in itemWithCommands.LocalCommands)
                    yield return ($"{item.ShortDescription} (inventory)", cmd);
            }
        }

        // Equipped item commands
        var equippedItems = _state.Equipment.GetAllEquipped(playerId);
        foreach (var (slot, equippedItemId) in equippedItems)
        {
            if (inventory.Contains(equippedItemId)) continue;

            var equippedItem = _state.Objects?.Get<IItem>(equippedItemId);
            if (equippedItem is IHasCommands equippedWithCommands)
            {
                foreach (var localCmd in equippedWithCommands.LocalCommands)
                    yield return ($"{equippedItem.ShortDescription} (equipped)", localCmd);
            }
        }
    }

    /// <summary>
    /// Check if a command name matches any local command (for validation).
    /// </summary>
    public bool HasCommand(string playerId, string command)
    {
        return GetAvailableCommands(playerId)
            .Any(x => MatchesCommand(x.Command, command));
    }

    /// <summary>
    /// Get help text for a specific local command.
    /// </summary>
    public string? GetCommandHelp(string playerId, string command)
    {
        var match = GetAvailableCommands(playerId)
            .FirstOrDefault(x => MatchesCommand(x.Command, command));

        if (match.Command is null)
            return null;

        var aliases = match.Command.Aliases.Count > 0
            ? $" (aliases: {string.Join(", ", match.Command.Aliases)})"
            : "";

        return $"{match.Command.Usage}{aliases}\n  {match.Command.Description}\n  Source: {match.Source}";
    }

    private LocalCommandInfo? FindCommand(IReadOnlyList<LocalCommandInfo> commands, string input)
    {
        foreach (var cmd in commands)
        {
            if (MatchesCommand(cmd, input))
                return cmd;
        }
        return null;
    }

    private static bool MatchesCommand(LocalCommandInfo cmd, string input)
    {
        if (cmd.Name.Equals(input, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in cmd.Aliases)
        {
            if (alias.Equals(input, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task ExecuteWithTimeoutAsync(
        IHasCommands provider,
        string command,
        string[] args,
        string playerId,
        IMudContext ctx)
    {
        try
        {
            var task = provider.HandleLocalCommandAsync(command, args, playerId, ctx);
            var completed = await Task.WhenAny(task, Task.Delay(_timeout));

            if (completed != task)
            {
                Console.WriteLine($"[LocalCommand:{command}] Timeout after {_timeout.TotalSeconds:F1}s");
            }
            else if (task.IsFaulted && task.Exception is not null)
            {
                var inner = task.Exception.InnerException ?? task.Exception;
                Console.WriteLine($"[LocalCommand:{command}] Error: {inner.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalCommand:{command}] Error: {ex.Message}");
        }
    }
}
