namespace JitRealm.Mud;

using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// Holds a compiled blueprint: the assembly, type, and metadata.
/// Shared by all instances created from this blueprint.
/// </summary>
public sealed class BlueprintHandle
{
    public required string BlueprintId { get; init; }
    public required AssemblyLoadContext Alc { get; init; }
    public required Assembly Assembly { get; init; }
    public required Type ObjectType { get; init; }
    public required DateTime SourceMtime { get; init; }

    private int _nextCloneNumber = 1;

    /// <summary>
    /// Gets the next unique clone number for this blueprint.
    /// </summary>
    public int GetNextCloneNumber() => _nextCloneNumber++;

    /// <summary>
    /// Count of active instances using this blueprint.
    /// When zero, blueprint can be safely unloaded.
    /// </summary>
    public int InstanceCount { get; set; }
}
