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
}

