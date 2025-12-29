using System.Text.Json;

namespace JitRealm.Mud.Persistence;

/// <summary>
/// Serializable player state.
/// </summary>
public sealed class PlayerSaveData
{
    public required string Name { get; init; }
    public string? LocationId { get; init; }
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
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public DateTimeOffset SavedAt { get; init; }
    public PlayerSaveData? Player { get; init; }
    public List<InstanceSaveData>? Instances { get; init; }
    public ContainerSaveData? Containers { get; init; }
}
