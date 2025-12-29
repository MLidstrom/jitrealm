namespace JitRealm.Mud;

/// <summary>
/// Optional base class for world objects. The driver can assign Id internally.
/// World code can still override Name and implement hooks/interfaces.
/// </summary>
public abstract class MudObjectBase : IMudObject
{
    public string Id { get; internal set; } = string.Empty;

    public abstract string Name { get; }

    public virtual void Create(WorldState state)
    {
        // Legacy hook for the minimal kernel.
        // Future versions will prefer IMudContext + IOnLoad.
    }
}
