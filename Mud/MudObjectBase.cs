namespace JitRealm.Mud;

/// <summary>
/// Optional base class for world objects. The driver can assign Id internally.
/// World code can still override Name and implement hooks/interfaces.
/// </summary>
public abstract class MudObjectBase : IMudObject
{
    private static readonly IReadOnlyDictionary<string, string> EmptyDetails =
        new Dictionary<string, string>();

    public string Id { get; internal set; } = string.Empty;

    public abstract string Name { get; }

    /// <summary>
    /// Detailed descriptions for parts of this object.
    /// Override in subclasses to provide "look at X" descriptions.
    /// Default returns an empty dictionary.
    /// </summary>
    public virtual IReadOnlyDictionary<string, string> Details => EmptyDetails;

    public virtual void Create(WorldState state)
    {
        // Legacy hook for the minimal kernel.
        // Future versions will prefer IMudContext + IOnLoad.
    }
}
