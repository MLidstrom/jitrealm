namespace JitRealm.Mud.Formatting;

/// <summary>
/// Data for the status bar display.
/// </summary>
public sealed record StatusBarData
{
    /// <summary>
    /// Player's display name.
    /// </summary>
    public required string PlayerName { get; init; }

    /// <summary>
    /// Current room/location name.
    /// </summary>
    public required string Location { get; init; }

    /// <summary>
    /// Current hit points.
    /// </summary>
    public required int HP { get; init; }

    /// <summary>
    /// Maximum hit points.
    /// </summary>
    public required int MaxHP { get; init; }

    /// <summary>
    /// Current combat target name, if in combat.
    /// </summary>
    public string? CombatTarget { get; init; }

    /// <summary>
    /// Combat target's current HP, if known.
    /// </summary>
    public int? TargetHP { get; init; }

    /// <summary>
    /// Combat target's max HP, if known.
    /// </summary>
    public int? TargetMaxHP { get; init; }

    /// <summary>
    /// Whether the player has wizard privileges.
    /// </summary>
    public bool IsWizard { get; init; }

    /// <summary>
    /// Player's current level.
    /// </summary>
    public int Level { get; init; } = 1;
}
