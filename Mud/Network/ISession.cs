namespace JitRealm.Mud.Network;

/// <summary>
/// Abstraction for a player session (console, telnet, websocket, etc.)
/// </summary>
public interface ISession
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// The ID of the player world object associated with this session.
    /// This is the instance ID of the cloned player object (e.g., "std/player#000001").
    /// </summary>
    string? PlayerId { get; set; }

    /// <summary>
    /// The player's display name (cached for efficiency).
    /// </summary>
    string? PlayerName { get; set; }

    /// <summary>
    /// Whether this session has wizard privileges.
    /// </summary>
    bool IsWizard { get; set; }

    /// <summary>
    /// Whether the session is still connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Write a line of text to the session output.
    /// </summary>
    Task WriteLineAsync(string text);

    /// <summary>
    /// Write text without newline to the session output.
    /// </summary>
    Task WriteAsync(string text);

    /// <summary>
    /// Read a line of input from the session.
    /// Returns null if the session is disconnected or no input available.
    /// </summary>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if input is available without blocking.
    /// </summary>
    bool HasPendingInput { get; }

    /// <summary>
    /// Close the session.
    /// </summary>
    Task CloseAsync();
}
