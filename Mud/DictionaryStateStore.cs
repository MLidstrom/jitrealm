using System.Collections.Concurrent;
using System.Text.Json;

namespace JitRealm.Mud;

public sealed class DictionaryStateStore : IStateStore
{
    private readonly ConcurrentDictionary<string, object?> _data = new();

    public T? Get<T>(string key)
    {
        if (!_data.TryGetValue(key, out var value))
            return default;

        // Handle JsonElement (from deserialization)
        if (value is JsonElement je)
        {
            return JsonSerializer.Deserialize<T>(je.GetRawText());
        }

        return value is T t ? t : default;
    }

    public void Set<T>(string key, T? value) => _data[key] = value;

    public bool Remove(string key) => _data.TryRemove(key, out _);

    public IEnumerable<string> Keys => _data.Keys;

    /// <summary>
    /// Export all data as JSON elements for serialization.
    /// </summary>
    public Dictionary<string, JsonElement> ToJsonDictionary()
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var kvp in _data)
        {
            if (kvp.Value is null)
                continue;

            var json = JsonSerializer.Serialize(kvp.Value);
            result[kvp.Key] = JsonSerializer.Deserialize<JsonElement>(json);
        }
        return result;
    }

    /// <summary>
    /// Import data from JSON elements (used during restore).
    /// </summary>
    public void FromJsonDictionary(Dictionary<string, JsonElement>? data)
    {
        _data.Clear();
        if (data is null)
            return;

        foreach (var kvp in data)
        {
            _data[kvp.Key] = kvp.Value;
        }
    }
}
