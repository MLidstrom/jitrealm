namespace JitRealm.Mud;

/// <summary>
/// Holds a runtime instance: the object, its state store, and reference to its blueprint.
/// </summary>
public sealed class InstanceHandle
{
    public required ObjectId Id { get; init; }
    public required BlueprintHandle Blueprint { get; init; }
    public required IMudObject Instance { get; init; }
    public required IStateStore State { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
