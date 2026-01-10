namespace JitRealm.Mud;

/// <summary>
/// Registry for daemon singleton instances.
/// Manages loading, lookup, and lifecycle of daemons.
/// </summary>
public sealed class DaemonRegistry
{
    private readonly Dictionary<string, IDaemon> _daemons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _idToInstanceId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Number of registered daemons.
    /// </summary>
    public int Count => _daemons.Count;

    /// <summary>
    /// Register a daemon instance.
    /// </summary>
    /// <param name="daemon">The daemon to register.</param>
    /// <param name="instanceId">The world object instance ID.</param>
    public void Register(IDaemon daemon, string instanceId)
    {
        if (string.IsNullOrWhiteSpace(daemon.DaemonId))
            throw new ArgumentException("Daemon must have a non-empty DaemonId", nameof(daemon));

        _daemons[daemon.DaemonId] = daemon;
        _idToInstanceId[daemon.DaemonId] = instanceId;
    }

    /// <summary>
    /// Unregister a daemon.
    /// </summary>
    /// <param name="daemonId">The daemon ID to unregister.</param>
    public void Unregister(string daemonId)
    {
        _daemons.Remove(daemonId);
        _idToInstanceId.Remove(daemonId);
    }

    /// <summary>
    /// Get a daemon by its ID.
    /// </summary>
    /// <typeparam name="T">The expected daemon type.</typeparam>
    /// <param name="daemonId">The daemon ID (e.g., "TIME_D").</param>
    /// <returns>The daemon or null if not found.</returns>
    public T? Get<T>(string daemonId) where T : class, IDaemon
    {
        return _daemons.TryGetValue(daemonId, out var daemon) ? daemon as T : null;
    }

    /// <summary>
    /// Get a daemon by its ID (non-generic version).
    /// </summary>
    /// <param name="daemonId">The daemon ID (e.g., "TIME_D").</param>
    /// <returns>The daemon or null if not found.</returns>
    public IDaemon? Get(string daemonId)
    {
        return _daemons.TryGetValue(daemonId, out var daemon) ? daemon : null;
    }

    /// <summary>
    /// Check if a daemon is registered.
    /// </summary>
    /// <param name="daemonId">The daemon ID to check.</param>
    /// <returns>True if registered.</returns>
    public bool IsRegistered(string daemonId)
    {
        return _daemons.ContainsKey(daemonId);
    }

    /// <summary>
    /// Get the world object instance ID for a daemon.
    /// </summary>
    /// <param name="daemonId">The daemon ID.</param>
    /// <returns>The instance ID or null if not found.</returns>
    public string? GetInstanceId(string daemonId)
    {
        return _idToInstanceId.TryGetValue(daemonId, out var id) ? id : null;
    }

    /// <summary>
    /// List all registered daemon IDs.
    /// </summary>
    public IEnumerable<string> ListDaemonIds() => _daemons.Keys.OrderBy(x => x);

    /// <summary>
    /// List all registered daemons.
    /// </summary>
    public IEnumerable<IDaemon> ListDaemons() => _daemons.Values;

    /// <summary>
    /// Clear all registered daemons.
    /// </summary>
    public void Clear()
    {
        _daemons.Clear();
        _idToInstanceId.Clear();
    }
}
