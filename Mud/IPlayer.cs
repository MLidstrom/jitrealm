namespace JitRealm.Mud;

/// <summary>
/// Interface for player objects. Players are world objects that extend ILiving
/// and represent actual connected players in the game.
/// </summary>
public interface IPlayer : ILiving, IHasInventory
{
    /// <summary>
    /// The player's display name.
    /// </summary>
    string PlayerName { get; }

    /// <summary>
    /// When the player last logged in.
    /// </summary>
    DateTimeOffset LastLogin { get; }

    /// <summary>
    /// Total time played in this session (since last login).
    /// </summary>
    TimeSpan SessionTime { get; }

    /// <summary>
    /// Current experience points.
    /// </summary>
    int Experience { get; }

    /// <summary>
    /// Current level.
    /// </summary>
    int Level { get; }

    /// <summary>
    /// Called when the player logs in.
    /// </summary>
    void OnLogin(IMudContext ctx);

    /// <summary>
    /// Called when the player logs out.
    /// </summary>
    void OnLogout(IMudContext ctx);

    /// <summary>
    /// Award experience points to the player.
    /// </summary>
    void AwardExperience(int amount, IMudContext ctx);
}
