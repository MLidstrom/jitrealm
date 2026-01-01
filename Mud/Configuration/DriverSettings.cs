namespace JitRealm.Mud.Configuration;

/// <summary>
/// Configuration settings for the MUD driver.
/// Loaded from appsettings.json at startup.
/// </summary>
public sealed class DriverSettings
{
    /// <summary>
    /// Server network settings.
    /// </summary>
    public ServerSettings Server { get; set; } = new();

    /// <summary>
    /// Path configuration settings.
    /// </summary>
    public PathSettings Paths { get; set; } = new();

    /// <summary>
    /// Game loop timing settings.
    /// </summary>
    public GameLoopSettings GameLoop { get; set; } = new();

    /// <summary>
    /// Combat system settings.
    /// </summary>
    public CombatSettings Combat { get; set; } = new();

    /// <summary>
    /// Security and sandboxing settings.
    /// </summary>
    public SecuritySettings Security { get; set; } = new();

    /// <summary>
    /// Player default settings.
    /// </summary>
    public PlayerSettings Player { get; set; } = new();

    /// <summary>
    /// Performance and resource-usage tuning.
    /// </summary>
    public PerformanceSettings Performance { get; set; } = new();

    /// <summary>
    /// LLM (AI) settings for NPC conversations and behavior.
    /// </summary>
    public LlmSettings Llm { get; set; } = new();

    /// <summary>
    /// Display and formatting settings.
    /// </summary>
    public DisplaySettings Display { get; set; } = new();
}

/// <summary>
/// Performance tuning settings. Defaults are conservative for server mode.
/// </summary>
public sealed class PerformanceSettings
{
    /// <summary>
    /// If true, the driver may trigger full blocking GC cycles after unload/reload operations.
    /// This can help reclaim collectible AssemblyLoadContext memory, but it is CPU-expensive.
    /// </summary>
    public bool ForceGcOnUnload { get; set; } = false;

    /// <summary>
    /// If ForceGcOnUnload is enabled, run a forced GC only every N unload/reload operations.
    /// </summary>
    public int ForceGcEveryNUnloads { get; set; } = 25;
}

/// <summary>
/// Server network settings.
/// </summary>
public sealed class ServerSettings
{
    /// <summary>
    /// TCP port for the telnet server.
    /// </summary>
    public int Port { get; set; } = 4000;

    /// <summary>
    /// Maximum concurrent connections (0 = unlimited).
    /// </summary>
    public int MaxConnections { get; set; } = 0;

    /// <summary>
    /// Welcome message shown to players on connect.
    /// Use {PlayerName} as placeholder.
    /// </summary>
    public string WelcomeMessage { get; set; } = "Welcome to JitRealm, {PlayerName}!";

    /// <summary>
    /// Name of the MUD displayed in startup messages.
    /// </summary>
    public string MudName { get; set; } = "JitRealm";

    /// <summary>
    /// Version string displayed in startup messages.
    /// </summary>
    public string Version { get; set; } = "0.15";
}

/// <summary>
/// Path configuration settings.
/// </summary>
public sealed class PathSettings
{
    /// <summary>
    /// Directory containing world object source files (relative to base directory).
    /// </summary>
    public string WorldDirectory { get; set; } = "World";

    /// <summary>
    /// Directory for save files (relative to base directory).
    /// </summary>
    public string SaveDirectory { get; set; } = "save";

    /// <summary>
    /// Name of the world save file.
    /// </summary>
    public string SaveFileName { get; set; } = "world.json";

    /// <summary>
    /// ID of the starting room for new players.
    /// </summary>
    public string StartRoom { get; set; } = "Rooms/start";

    /// <summary>
    /// Blueprint ID for new player objects.
    /// </summary>
    public string PlayerBlueprint { get; set; } = "std/player";

    /// <summary>
    /// Directory for player account files (relative to base directory).
    /// </summary>
    public string PlayersDirectory { get; set; } = "players";
}

/// <summary>
/// Game loop timing settings.
/// </summary>
public sealed class GameLoopSettings
{
    /// <summary>
    /// Delay in milliseconds between game loop iterations.
    /// Lower values = more responsive but higher CPU usage.
    /// </summary>
    public int LoopDelayMs { get; set; } = 50;

    /// <summary>
    /// Default heartbeat interval in seconds for objects with IHeartbeat.
    /// </summary>
    public int DefaultHeartbeatSeconds { get; set; } = 2;

    /// <summary>
    /// Enable auto-save on a timer.
    /// </summary>
    public bool AutoSaveEnabled { get; set; } = false;

    /// <summary>
    /// Auto-save interval in minutes (if enabled).
    /// </summary>
    public int AutoSaveIntervalMinutes { get; set; } = 15;
}

/// <summary>
/// Combat system settings.
/// </summary>
public sealed class CombatSettings
{
    /// <summary>
    /// Interval between combat rounds in seconds.
    /// </summary>
    public int RoundIntervalSeconds { get; set; } = 3;

    /// <summary>
    /// Base chance to successfully flee from combat (0-100).
    /// </summary>
    public int FleeChancePercent { get; set; } = 50;

    /// <summary>
    /// Base damage when fighting unarmed.
    /// </summary>
    public int UnarmedMinDamage { get; set; } = 1;

    /// <summary>
    /// Maximum unarmed damage.
    /// </summary>
    public int UnarmedMaxDamage { get; set; } = 3;
}

/// <summary>
/// Security and sandboxing settings.
/// </summary>
public sealed class SecuritySettings
{
    /// <summary>
    /// Timeout for hook invocations in milliseconds.
    /// Prevents infinite loops in world code.
    /// </summary>
    public int HookTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Timeout for callout method invocations in milliseconds.
    /// </summary>
    public int CallOutTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Timeout for heartbeat invocations in milliseconds.
    /// </summary>
    public int HeartbeatTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// Enable verbose security logging.
    /// </summary>
    public bool VerboseLogging { get; set; } = false;
}

/// <summary>
/// Default player settings.
/// </summary>
public sealed class PlayerSettings
{
    /// <summary>
    /// Starting HP for new players.
    /// </summary>
    public int StartingHP { get; set; } = 100;

    /// <summary>
    /// Maximum carry capacity in weight units.
    /// </summary>
    public int CarryCapacity { get; set; } = 100;

    /// <summary>
    /// HP regeneration per heartbeat.
    /// </summary>
    public int RegenPerHeartbeat { get; set; } = 1;

    /// <summary>
    /// XP multiplier for level progression.
    /// </summary>
    public double XpMultiplier { get; set; } = 1.5;

    /// <summary>
    /// Base XP required per level.
    /// </summary>
    public int BaseXpPerLevel { get; set; } = 100;
}

/// <summary>
/// LLM settings for NPC AI.
/// </summary>
public sealed class LlmSettings
{
    /// <summary>
    /// Enable LLM-powered NPC behavior.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// LLM provider: "ollama", "openai", "anthropic".
    /// </summary>
    public string Provider { get; set; } = "ollama";

    /// <summary>
    /// Ollama server URL.
    /// </summary>
    public string OllamaUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Model name to use (e.g., "llama3.2:7b", "mistral").
    /// </summary>
    public string Model { get; set; } = "llama3.2:7b";

    /// <summary>
    /// Temperature for response generation (0.0-2.0).
    /// Lower = more deterministic, higher = more creative.
    /// </summary>
    public double Temperature { get; set; } = 0.8;

    /// <summary>
    /// Maximum tokens in response.
    /// </summary>
    public int MaxTokens { get; set; } = 150;

    /// <summary>
    /// Timeout for LLM requests in milliseconds.
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Directory containing NPC prompt files (relative to World directory).
    /// </summary>
    public string PromptsDirectory { get; set; } = "npcs";
}

/// <summary>
/// Display and formatting settings.
/// </summary>
public sealed class DisplaySettings
{
    /// <summary>
    /// Enable ANSI color codes by default for new sessions.
    /// </summary>
    public bool DefaultColorsEnabled { get; set; } = true;
}
