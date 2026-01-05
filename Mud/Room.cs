namespace JitRealm.Mud;

public interface IRoom : IMudObject
{
    new string Description { get; }
    IReadOnlyDictionary<string, string> Exits { get; }
    IReadOnlyList<string> Contents { get; }
}
