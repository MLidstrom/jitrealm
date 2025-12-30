using JitRealm.Mud.Network;

namespace JitRealm.Mud.Commands;

/// <summary>
/// Context passed to commands providing access to world state and player information.
/// </summary>
public class CommandContext
{
    /// <summary>
    /// The world state containing all game data.
    /// </summary>
    public required WorldState State { get; init; }

    /// <summary>
    /// The player's object ID.
    /// </summary>
    public required string PlayerId { get; init; }

    /// <summary>
    /// The session for this player.
    /// </summary>
    public required ISession Session { get; init; }

    /// <summary>
    /// Action to output text to the player.
    /// </summary>
    public required Action<string> Output { get; init; }

    /// <summary>
    /// Function to create an IMudContext for world objects.
    /// </summary>
    public required Func<string, MudContext> CreateContext { get; init; }

    /// <summary>
    /// The raw input string that triggered this command.
    /// </summary>
    public string RawInput { get; init; } = "";

    /// <summary>
    /// Get the player's current location (room ID).
    /// </summary>
    public string? GetPlayerLocation()
    {
        return State.Containers.GetContainer(PlayerId);
    }

    /// <summary>
    /// Get the player object.
    /// </summary>
    public IPlayer? GetPlayer()
    {
        return State.Objects?.Get<IPlayer>(PlayerId);
    }

    /// <summary>
    /// Get the current room.
    /// </summary>
    public IRoom? GetCurrentRoom()
    {
        var roomId = GetPlayerLocation();
        return roomId is not null ? State.Objects?.Get<IRoom>(roomId) : null;
    }

    /// <summary>
    /// Whether the current player is a wizard.
    /// </summary>
    public bool IsWizard => Session.IsWizard;

    /// <summary>
    /// Output a formatted error message.
    /// </summary>
    public void Error(string message)
    {
        Output($"Error: {message}");
    }
}
