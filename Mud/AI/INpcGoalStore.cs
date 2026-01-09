using System.Text.Json;

namespace JitRealm.Mud.AI;

public interface INpcGoalStore
{
    /// <summary>
    /// Upsert a goal. If a goal with the same NpcId and GoalType exists, it is updated.
    /// </summary>
    Task UpsertAsync(NpcGoal goal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the highest priority (lowest importance number) goal for an NPC.
    /// </summary>
    Task<NpcGoal?> GetAsync(string npcId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all goals for an NPC, ordered by importance (lowest first = highest priority).
    /// </summary>
    Task<IReadOnlyList<NpcGoal>> GetAllAsync(string npcId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear a specific goal type for an NPC.
    /// </summary>
    Task ClearAsync(string npcId, string goalType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear all goals for an NPC.
    /// Note: "survive" is treated as a drive (not a persisted goal) and is not required for gameplay.
    /// </summary>
    Task ClearAllAsync(string npcId, bool preserveSurvival = true, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an NPC goal with priority (importance).
/// Lower importance = higher priority.
/// </summary>
public sealed record NpcGoal(
    string NpcId,
    string GoalType,
    string? TargetPlayer,
    JsonDocument Params,
    string Status,
    int Importance,  // 1 = survival (highest), 10 = default, higher = lower priority
    DateTimeOffset UpdatedAt);

/// <summary>
/// Well-known goal importance levels.
/// </summary>
public static class GoalImportance
{
    public const int Combat = 5;        // Active combat situations
    public const int Urgent = 10;       // Urgent tasks
    public const int Default = 50;      // Normal priority
    public const int Background = 100;  // Low priority background tasks
}

/// <summary>
/// Interface for NPCs that have a default goal defined in their source code.
/// The goal is set when the NPC is first loaded (if no goal already exists).
/// </summary>
public interface IHasDefaultGoal
{
    /// <summary>
    /// The default goal type for this NPC (e.g., "sell_items", "guard_area", "help_customers").
    /// Return null to have no default goal.
    /// </summary>
    string? DefaultGoalType { get; }

    /// <summary>
    /// Optional target player for the default goal.
    /// </summary>
    string? DefaultGoalTarget => null;

    /// <summary>
    /// Optional JSON parameters for the default goal.
    /// </summary>
    string DefaultGoalParams => "{}";

    /// <summary>
    /// Importance of the default goal. Lower = higher priority.
    /// Default is 50 (normal priority). Use GoalImportance constants.
    /// </summary>
    int DefaultGoalImportance => GoalImportance.Default;
}


