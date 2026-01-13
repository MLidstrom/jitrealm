using System.Text.Json;

namespace JitRealm.Mud.AI;

public interface INpcNeedStore
{
    Task UpsertAsync(NpcNeed need, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NpcNeed>> GetAllAsync(string npcId, CancellationToken cancellationToken = default);
    Task ClearAsync(string npcId, string needType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an NPC need/drive. Lower level = higher priority. Level 1 is the top drive.
/// </summary>
public sealed record NpcNeed(
    string NpcId,
    string NeedType,
    int Level,
    JsonDocument Params,
    string Status,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Well-known need levels. Lower = higher priority.
/// </summary>
public static class NeedLevel
{
    /// <summary>Level 1: Survival - stay alive (auto-applied to all living entities)</summary>
    public const int Survival = 1;

    /// <summary>Level 2: Primary drive - core motivation for this NPC type</summary>
    public const int Primary = 2;

    /// <summary>Level 3: Secondary drive - important but not core</summary>
    public const int Secondary = 3;

    /// <summary>Level 4: Tertiary drive - nice to have</summary>
    public const int Tertiary = 4;

    /// <summary>Level 5: Background drive - lowest priority</summary>
    public const int Background = 5;
}

/// <summary>
/// Interface for NPCs that have default needs/drives defined in their source code.
/// Needs are always-on motivations that never complete (unlike goals).
/// </summary>
public interface IHasDefaultNeeds
{
    /// <summary>
    /// Default needs for this NPC, as (needType, level) tuples.
    /// Level 1 is highest priority. "survive" at level 1 is auto-added for all living entities.
    /// Example: new[] { ("hunt", 2), ("rest", 3) }
    /// </summary>
    IReadOnlyList<(string NeedType, int Level)> DefaultNeeds { get; }

    /// <summary>
    /// Get a plan template for a goal type. Return null to not auto-apply a template.
    /// Plan steps are pipe-separated: "step1|step2|step3"
    /// </summary>
    /// <param name="goalType">The goal type to get a template for.</param>
    /// <returns>Pipe-separated plan steps, or null.</returns>
    string? GetPlanTemplateForGoal(string goalType) => null;
}

/// <summary>
/// Optional interface for NPCs that map needs to goals.
/// When no active goals exist, the system derives a goal from the NPC's top need.
/// </summary>
public interface IHasNeedGoalMapping
{
    /// <summary>
    /// Get a goal type for a given need type.
    /// Return null to use default convention (need type becomes goal type).
    /// </summary>
    /// <param name="needType">The need type to derive a goal from.</param>
    /// <returns>The goal type, or null to use convention.</returns>
    string? GetGoalForNeed(string needType);

    /// <summary>
    /// Get a plan template for a goal derived from a need.
    /// Return null to not suggest a template.
    /// </summary>
    /// <param name="goalType">The derived goal type.</param>
    /// <returns>Pipe-separated plan steps, or null.</returns>
    string? GetPlanTemplateForGoal(string goalType) => null;
}

/// <summary>
/// Interface for NPCs that wander between key locations instead of randomly.
/// NPCs stay at each location for a configurable dwell time before moving on.
/// </summary>
public interface IHasKeyLocations
{
    /// <summary>
    /// Default key locations this NPC patrols between. Room names (for fuzzy matching).
    /// Used when no goal-specific locations are defined.
    /// </summary>
    IReadOnlyList<string> DefaultKeyLocations { get; }

    /// <summary>
    /// Get locations for a specific goal type. Return null to use DefaultKeyLocations.
    /// </summary>
    /// <param name="goalType">The current goal type.</param>
    /// <returns>List of room names, or null to use defaults.</returns>
    IReadOnlyList<string>? GetLocationsForGoal(string goalType) => null;

    /// <summary>
    /// How long to stay at each location (min/max in seconds).
    /// Default: (300, 600) = 5-10 minutes
    /// </summary>
    (int MinSeconds, int MaxSeconds) DwellDuration => (300, 600);

    /// <summary>
    /// Whether to visit locations in random order or sequentially.
    /// Default: true (random)
    /// </summary>
    bool RandomizeOrder => true;
}

