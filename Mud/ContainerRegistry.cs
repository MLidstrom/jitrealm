namespace JitRealm.Mud;

/// <summary>
/// Driver-managed container system. Tracks which objects are inside which containers.
/// Used for room contents, inventories, bags, etc.
/// </summary>
public sealed class ContainerRegistry
{
    // containerId -> set of member objectIds
    private readonly Dictionary<string, HashSet<string>> _contents = new(StringComparer.OrdinalIgnoreCase);

    // objectId -> its container (inverse lookup)
    private readonly Dictionary<string, string> _location = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Add an object to a container. Removes from previous container if any.
    /// </summary>
    public void Add(string containerId, string objectId)
    {
        // Remove from old container if any
        if (_location.TryGetValue(objectId, out var oldContainer))
        {
            if (_contents.TryGetValue(oldContainer, out var oldSet))
                oldSet.Remove(objectId);
        }

        // Add to new container
        if (!_contents.TryGetValue(containerId, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _contents[containerId] = set;
        }
        set.Add(objectId);
        _location[objectId] = containerId;
    }

    /// <summary>
    /// Remove an object from any container it's in.
    /// </summary>
    public void Remove(string objectId)
    {
        if (_location.TryGetValue(objectId, out var container))
        {
            if (_contents.TryGetValue(container, out var set))
                set.Remove(objectId);
            _location.Remove(objectId);
        }
    }

    /// <summary>
    /// Get the contents of a container.
    /// </summary>
    public IReadOnlyCollection<string> GetContents(string containerId)
    {
        return _contents.TryGetValue(containerId, out var set)
            ? set
            : Array.Empty<string>();
    }

    /// <summary>
    /// Get the container an object is in, or null if not in any container.
    /// </summary>
    public string? GetContainer(string objectId)
    {
        return _location.TryGetValue(objectId, out var c) ? c : null;
    }

    /// <summary>
    /// Export all container data for serialization.
    /// </summary>
    public Dictionary<string, List<string>> ToSerializable()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _contents)
        {
            if (kvp.Value.Count > 0)
            {
                result[kvp.Key] = kvp.Value.ToList();
            }
        }
        return result;
    }

    /// <summary>
    /// Import container data from serialization.
    /// </summary>
    public void FromSerializable(Dictionary<string, List<string>>? data)
    {
        _contents.Clear();
        _location.Clear();

        if (data is null)
            return;

        foreach (var kvp in data)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var objectId in kvp.Value)
            {
                set.Add(objectId);
                _location[objectId] = kvp.Key;
            }
            _contents[kvp.Key] = set;
        }
    }
}
