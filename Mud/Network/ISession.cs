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
    /// The player associated with this session.
    /// </summary>
    Player? Player { get; set; }

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
