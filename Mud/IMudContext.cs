namespace JitRealm.Mud;

/// <summary>
/// Driver-provided context passed into lifecycle hooks and command handlers.
/// This is the primary API surface for world code (lpMUD driver boundary).
/// </summary>
public interface IMudContext
{
    WorldState World { get; }
    IStateStore State { get; }
    IClock Clock { get; }

    /// <summary>
    /// Send a private message to a specific target.
    /// </summary>
    void Tell(string targetId, string message);

    /// <summary>
    /// Broadcast a message to everyone in the current room.
    /// </summary>
    void Say(string message);

    /// <summary>
    /// Broadcast an emote/action to everyone in the current room.
    /// </summary>
    void Emote(string action);

    /// <summary>
    /// Schedule a delayed method call on the current object.
    /// </summary>
    /// <param name="methodName">Name of the method to call (must accept IMudContext)</param>
    /// <param name="delay">Time to wait before calling</param>
    /// <param name="args">Optional arguments to pass</param>
    /// <returns>Callout ID for cancellation</returns>
    long CallOut(string methodName, TimeSpan delay, params object?[] args);

    /// <summary>
    /// Schedule a repeating method call on the current object.
    /// </summary>
    /// <param name="methodName">Name of the method to call</param>
    /// <param name="interval">Time between calls</param>
    /// <param name="args">Optional arguments to pass</param>
    /// <returns>Callout ID for cancellation</returns>
    long Every(string methodName, TimeSpan interval, params object?[] args);

    /// <summary>
    /// Cancel a scheduled callout.
    /// </summary>
    /// <param name="calloutId">ID returned by CallOut or Every</param>
    /// <returns>True if cancelled, false if not found</returns>
    bool CancelCallOut(long calloutId);
}
