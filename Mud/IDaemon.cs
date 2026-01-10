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
