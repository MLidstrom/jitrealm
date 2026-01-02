namespace JitRealm.Mud;

/// <summary>
/// Interface for objects that provide local commands.
/// Local commands are context-sensitive and only available when
/// the player is in the room or has the item in inventory/equipped.
/// </summary>
public interface IHasCommands
{
    /// <summary>
    /// Commands provided by this object.
    /// </summary>
    IReadOnlyList<LocalCommandInfo> LocalCommands { get; }

    /// <summary>
    /// Handle execution of a local command.
    /// </summary>
    /// <param name="command">The command name (lowercase)</param>
    /// <param name="args">Arguments passed to the command</param>
    /// <param name="playerId">The ID of the player executing the command</param>
    /// <param name="ctx">The mud context for world operations</param>
    Task HandleLocalCommandAsync(string command, string[] args, string playerId, IMudContext ctx);
}

/// <summary>
/// Metadata for a local command provided by a room or item.
/// </summary>
/// <param name="Name">Primary command name (lowercase)</param>
/// <param name="Aliases">Alternative names for the command</param>
/// <param name="Usage">Usage syntax for help display (e.g., "buy &lt;item&gt;")</param>
/// <param name="Description">Short description for help</param>
public record LocalCommandInfo(
    string Name,
    IReadOnlyList<string> Aliases,
    string Usage,
    string Description
);
