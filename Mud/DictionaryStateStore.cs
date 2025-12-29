using System.Collections.Concurrent;

namespace JitRealm.Mud;

public sealed class DictionaryStateStore : IStateStore
{
    private readonly ConcurrentDictionary<string, object?> _data = new();

    public T? Get<T>(string key)
    {
        if (!_data.TryGetValue(key, out var value))
            return default;

        return value is T t ? t : default;
    }

    public void Set<T>(string key, T? value) => _data[key] = value;

    public bool Remove(string key) => _data.TryRemove(key, out _);

    public IEnumerable<string> Keys => _data.Keys;
}
