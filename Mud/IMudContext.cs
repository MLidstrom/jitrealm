namespace JitRealm.Mud;

/// <summary>
/// Driver-provided context passed into lifecycle hooks and command handlers.
/// This is the primary API surface for world code (lpMUD driver boundary).
/// </summary>
public interface IMudContext
{
    WorldState World { get; }
    IStateStore State { get; }
    IClock Clock { get; }
}
