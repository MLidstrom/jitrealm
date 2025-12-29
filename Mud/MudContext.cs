namespace JitRealm.Mud;

/// <summary>
/// Implementation of IMudContext, providing world code access to driver services.
/// </summary>
public sealed class MudContext : IMudContext
{
    public required WorldState World { get; init; }
    public required IStateStore State { get; init; }
    public required IClock Clock { get; init; }

    /// <summary>
    /// The ID of the current object this context is associated with.
    /// </summary>
    public string? CurrentObjectId { get; init; }
}
