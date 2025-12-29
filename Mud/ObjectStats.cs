namespace JitRealm.Mud;

/// <summary>
/// Statistics about a blueprint or instance, returned by stat command.
/// </summary>
public sealed class ObjectStats
{
    public required string Id { get; init; }
    public required bool IsBlueprint { get; init; }
    public required string BlueprintId { get; init; }
    public required string TypeName { get; init; }
    public DateTime? SourceMtime { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public int? InstanceCount { get; init; }
    public string[]? StateKeys { get; init; }
}
