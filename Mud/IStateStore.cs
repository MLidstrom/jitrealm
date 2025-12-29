namespace JitRealm.Mud;

/// <summary>
/// Per-instance state store, owned by the driver (not the world object instance).
/// This enables reload/migration without losing state.
/// </summary>
public interface IStateStore
{
    T? Get<T>(string key);
    void Set<T>(string key, T? value);
    bool Remove(string key);
}
