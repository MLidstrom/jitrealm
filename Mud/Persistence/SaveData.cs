using System.Text.Json;

namespace JitRealm.Mud.Persistence;

/// <summary>
/// Serializable session state for restoring player association.
/// </summary>
public sealed class SessionSaveData
{
    /// <summary>
    /// The instance ID of the player world object.
    /// </summary>
    public required string PlayerId { get; init; }

    /// <summary>
    /// The player's display name.
    /// </summary>
    public required string PlayerName { get; init; }
}

/// <summary>
/// Serializable instance state.
/// </summary>
public sealed class InstanceSaveData
{
    public required string InstanceId { get; init; }
    public required string BlueprintId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// State store data serialized as JSON elements for type preservation.
    /// </summary>
    public Dictionary<string, JsonElement>? State { get; init; }
}

/// <summary>
/// Serializable container registry state.
/// </summary>
public sealed class ContainerSaveData
{
    /// <summary>
    /// ContainerId -> list of member objectIds
    /// </summary>
    public Dictionary<string, List<string>>? Contents { get; init; }
}

/// <summary>
/// Root save data containing all world state.
/// </summary>
public sealed class WorldSaveData
{
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;
    public DateTimeOffset SavedAt { get; init; }

    /// <summary>
    /// Session data for restoring player association.
    /// For console mode, this is a single session.
    /// </summary>
    public SessionSaveData? Session { get; init; }

    public List<InstanceSaveData>? Instances { get; init; }
    public ContainerSaveData? Containers { get; init; }
}
