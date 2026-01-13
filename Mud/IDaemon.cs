namespace JitRealm.Mud;

/// <summary>
/// Interface for daemon objects - long-lived singleton service objects.
/// Daemons provide shared game systems like time, weather, economy, etc.
///
/// Unlike regular world objects:
/// - Daemons are singletons (only one instance per daemon type)
/// - Daemons are loaded automatically from World/daemons/ on startup
/// - Daemons are accessible via World.GetDaemon&lt;T&gt;() from any world code
/// - Daemons support heartbeat for periodic updates
///
/// Naming convention: TIME_D, WEATHER_D, ECONOMY_D, etc.
/// </summary>
public interface IDaemon : IMudObject
{
    /// <summary>
    /// Unique identifier for this daemon type.
    /// By convention, use uppercase with _D suffix: "TIME_D", "WEATHER_D", etc.
    /// This is used for lookups via World.GetDaemon().
    /// </summary>
    string DaemonId { get; }

    /// <summary>
    /// Called when the daemon is initialized during server startup.
    /// Use this to set up initial state.
    /// </summary>
    void Initialize(IMudContext ctx);
}

/// <summary>
/// Optional interface for daemons that need periodic updates.
/// </summary>
public interface IDaemonHeartbeat : IDaemon, IHeartbeat
{
}

/// <summary>
/// Optional interface for daemons that want to perform cleanup on shutdown.
/// </summary>
public interface IDaemonShutdown : IDaemon
{
    /// <summary>
    /// Called when the server is shutting down.
    /// Use this to save state or clean up resources.
    /// </summary>
    void Shutdown(IMudContext ctx);
}

/// <summary>
/// Interface for time-providing daemons.
/// The driver uses this to display time information in outdoor rooms.
/// </summary>
public interface ITimeDaemon : IDaemon
{
    /// <summary>
    /// Current world hour (0-23).
    /// </summary>
    int Hour { get; }

    /// <summary>
    /// Current world minute (0-59).
    /// </summary>
    int Minute { get; }

    /// <summary>
    /// Gets a formatted time string (e.g., "14:30").
    /// </summary>
    string TimeString { get; }

    /// <summary>
    /// Whether it's currently night time.
    /// </summary>
    bool IsNight { get; }

    /// <summary>
    /// Whether it's currently day time.
    /// </summary>
    bool IsDay { get; }

    /// <summary>
    /// Description of the current time period (for room descriptions).
    /// </summary>
    string PeriodDescription { get; }
}

/// <summary>
/// Interface for weather-providing daemons.
/// The driver uses this to display weather information in outdoor rooms.
/// </summary>
public interface IWeatherDaemon : IDaemon
{
    /// <summary>
    /// Whether it's currently raining.
    /// </summary>
    bool IsRaining { get; }

    /// <summary>
    /// Whether visibility is low (fog, heavy rain, blizzard).
    /// </summary>
    bool IsLowVisibility { get; }

    /// <summary>
    /// Whether the current weather is dangerous.
    /// </summary>
    bool IsDangerous { get; }

    /// <summary>
    /// Description of the current weather (for room descriptions).
    /// </summary>
    string WeatherDescription { get; }
}

/// <summary>
/// Interface for path-finding daemons.
/// Provides navigation assistance for NPCs to reach destination rooms.
/// </summary>
public interface IPathingDaemon : IDaemon
{
    /// <summary>
    /// Maximum search depth for pathfinding (to prevent runaway searches).
    /// </summary>
    int MaxSearchDepth { get; }

    /// <summary>
    /// Find a path from one room to another.
    /// </summary>
    /// <param name="fromRoomId">The starting room ID (blueprint or instance).</param>
    /// <param name="toRoomId">The destination room ID (blueprint or instance).</param>
    /// <returns>A PathResult containing the route, or an empty result if no path found.</returns>
    PathResult FindPath(string fromRoomId, string toRoomId);

    /// <summary>
    /// Get the next direction to move toward a destination.
    /// This is the primary API for NPC navigation.
    /// </summary>
    /// <param name="fromRoomId">The current room ID.</param>
    /// <param name="toRoomId">The destination room ID.</param>
    /// <returns>The direction to move, or null if already there or no path exists.</returns>
    string? GetNextDirection(string fromRoomId, string toRoomId);

    /// <summary>
    /// Check if a path exists between two rooms.
    /// </summary>
    /// <param name="fromRoomId">The starting room ID.</param>
    /// <param name="toRoomId">The destination room ID.</param>
    /// <returns>True if a path exists within MaxSearchDepth.</returns>
    bool HasPath(string fromRoomId, string toRoomId);

    /// <summary>
    /// Clear the path cache (e.g., after room layout changes).
    /// </summary>
    void ClearCache();
}

/// <summary>
/// Result of a pathfinding operation.
/// </summary>
public readonly struct PathResult
{
    /// <summary>
    /// Whether a path was found.
    /// </summary>
    public bool Found { get; init; }

    /// <summary>
    /// The list of directions to travel (e.g., ["north", "east", "up"]).
    /// Empty if no path found or already at destination.
    /// </summary>
    public IReadOnlyList<string> Directions { get; init; }

    /// <summary>
    /// The distance (number of rooms) to the destination.
    /// -1 if no path found.
    /// </summary>
    public int Distance { get; init; }

    /// <summary>
    /// Whether the search was truncated due to MaxSearchDepth.
    /// If true, a path might exist but wasn't found within the limit.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// Creates a successful path result.
    /// </summary>
    public static PathResult Success(IReadOnlyList<string> directions) => new()
    {
        Found = true,
        Directions = directions,
        Distance = directions.Count,
        Truncated = false
    };

    /// <summary>
    /// Creates a "no path found" result.
    /// </summary>
    public static PathResult NotFound(bool truncated = false) => new()
    {
        Found = false,
        Directions = Array.Empty<string>(),
        Distance = -1,
        Truncated = truncated
    };

    /// <summary>
    /// Creates an "already at destination" result.
    /// </summary>
    public static PathResult AlreadyThere => new()
    {
        Found = true,
        Directions = Array.Empty<string>(),
        Distance = 0,
        Truncated = false
    };
}
