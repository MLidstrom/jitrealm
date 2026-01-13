using System;
using System.IO;
using System.Threading.Tasks;
using JitRealm.Mud.AI;
using JitRealm.Mud.Diagnostics;
using JitRealm.Mud.Network;

namespace JitRealm.Mud;

public sealed class WorldState
{
    private readonly IClock _clock;
    private NpcCommandExecutor? _npcCommandExecutor;

    public WorldState(IClock? clock = null)
    {
        _clock = clock ?? new SystemClock();
        Heartbeats = new HeartbeatScheduler(_clock);
        CallOuts = new CallOutScheduler(_clock);
    }

    /// <summary>
    /// Command executor for NPCs to issue player-like commands.
    /// </summary>
    public NpcCommandExecutor NpcCommands => _npcCommandExecutor ??= new NpcCommandExecutor(this, _clock);

    /// <summary>
    /// Optional LLM service for AI-powered NPCs.
    /// Set during server initialization if LLM is enabled.
    /// </summary>
    public ILlmService? LlmService { get; set; }

    /// <summary>
    /// Optional persistent memory/goals system for NPCs and world knowledge.
    /// Set during server initialization if enabled.
    /// </summary>
    public NpcMemorySystem? MemorySystem { get; set; }

    /// <summary>
    /// Optional debug logger for LLM operations.
    /// Set during server initialization if debug logging is enabled.
    /// </summary>
    public LlmDebugLogger? LlmDebugger { get; set; }

    /// <summary>
    /// Registry of goal evaluators for deterministic step completion.
    /// Evaluators check if goal steps are complete without needing LLM.
    /// </summary>
    public GoalEvaluatorRegistry GoalEvaluators { get; } = GoalEvaluatorRegistry.CreateDefault();

    /// <summary>
    /// The clock backing schedulers and time-based systems.
    /// Prefer reusing this clock instead of allocating new clocks per operation.
    /// </summary>
    public IClock Clock => _clock;

    public ObjectManager? Objects { get; set; }
    public ContainerRegistry Containers { get; } = new();
    public EquipmentRegistry Equipment { get; } = new();
    public CombatScheduler Combat { get; } = new();
    public MessageQueue Messages { get; } = new();
    public HeartbeatScheduler Heartbeats { get; }
    public CallOutScheduler CallOuts { get; }
    public LoopMetrics Metrics { get; } = new();

    /// <summary>
    /// Session manager for multi-player mode.
    /// Each session has a PlayerId pointing to a cloned player world object.
    /// Player location is tracked via ContainerRegistry.
    /// </summary>
    public SessionManager Sessions { get; } = new();

    /// <summary>
    /// Event log for NPC awareness. Stores recent events per room.
    /// </summary>
    public RoomEventLog EventLog { get; } = new();

    /// <summary>
    /// Registry for daemon singleton instances.
    /// Daemons are long-lived service objects providing shared game systems.
    /// </summary>
    public DaemonRegistry Daemons { get; } = new();

    /// <summary>
    /// Create a MudContext for a specific object.
    /// </summary>
    /// <param name="objectId">The object ID to create the context for</param>
    /// <param name="clock">The clock to use</param>
    /// <returns>A MudContext configured for the object</returns>
    public MudContext CreateContext(string objectId, IClock clock)
    {
        return new MudContext(this, clock, LlmService)
        {
            State = Objects?.GetStateStore(objectId) ?? new DictionaryStateStore(),
            CurrentObjectId = objectId,
            RoomId = Containers.GetContainer(objectId)
        };
    }

    /// <summary>
    /// Convenience overload using the world's default clock.
    /// </summary>
    public MudContext CreateContext(string objectId) => CreateContext(objectId, _clock);

    /// <summary>
    /// Create a MudContext with an explicit room override (used by internal driver logic).
    /// </summary>
    public MudContext CreateContext(string objectId, IClock clock, string? roomIdOverride)
    {
        return new MudContext(this, clock, LlmService)
        {
            State = Objects?.GetStateStore(objectId) ?? new DictionaryStateStore(),
            CurrentObjectId = objectId,
            RoomId = roomIdOverride ?? Containers.GetContainer(objectId)
        };
    }

    /// <summary>
    /// Load all daemons from the World/daemons/ directory.
    /// Daemons are singleton service objects that provide shared game systems.
    /// </summary>
    /// <param name="worldRoot">Path to the World directory.</param>
    /// <returns>Number of daemons loaded.</returns>
    public async Task<int> LoadDaemonsAsync(string worldRoot)
    {
        if (Objects is null)
            return 0;

        var daemonsDir = Path.Combine(worldRoot, "daemons");
        if (!Directory.Exists(daemonsDir))
        {
            // Create the directory if it doesn't exist
            Directory.CreateDirectory(daemonsDir);
            return 0;
        }

        int loaded = 0;
        var daemonFiles = Directory.GetFiles(daemonsDir, "*.cs", SearchOption.AllDirectories);

        foreach (var filePath in daemonFiles)
        {
            try
            {
                // Convert to relative path for blueprint ID
                var relativePath = Path.GetRelativePath(worldRoot, filePath)
                    .Replace(Path.DirectorySeparatorChar, '/');

                // Load the daemon as a singleton
                var obj = await Objects.LoadAsync<IMudObject>(relativePath, this);

                if (obj is IDaemon daemon)
                {
                    // Register in daemon registry
                    Daemons.Register(daemon, obj.Id);

                    // Initialize the daemon
                    var ctx = CreateContext(obj.Id);
                    daemon.Initialize(ctx);

                    loaded++;
                    Console.WriteLine($"Loaded daemon: {daemon.DaemonId}");
                }
                else
                {
                    Console.WriteLine($"Warning: {relativePath} does not implement IDaemon");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load daemon from {filePath}: {ex.Message}");
            }
        }

        return loaded;
    }

    /// <summary>
    /// Shutdown all daemons that implement IDaemonShutdown.
    /// Call this when the server is shutting down.
    /// </summary>
    public void ShutdownDaemons()
    {
        foreach (var daemon in Daemons.ListDaemons())
        {
            if (daemon is IDaemonShutdown shutdownDaemon)
            {
                try
                {
                    var instanceId = Daemons.GetInstanceId(daemon.DaemonId);
                    if (instanceId is not null)
                    {
                        var ctx = CreateContext(instanceId);
                        shutdownDaemon.Shutdown(ctx);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error shutting down daemon {daemon.DaemonId}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Process spawns for a room that implements ISpawner.
    /// Creates any missing spawns up to the defined limits.
    /// </summary>
    /// <param name="roomId">The room ID to process spawns for</param>
    /// <param name="clock">The clock to use</param>
    /// <returns>Number of NPCs spawned</returns>
    public async Task<int> ProcessSpawnsAsync(string roomId, IClock clock)
    {
        if (Objects is null)
            return 0;

        // Get the room and check if it implements ISpawner
        var roomObj = Objects.Get<IRoom>(roomId);
        if (roomObj is not ISpawner spawner)
            return 0;

        int spawned = 0;

        foreach (var (blueprintId, maxCount) in spawner.Spawns)
        {
            // Count current spawns of this type globally (items can be picked up, NPCs can wander)
            var currentCount = CountSpawnsGlobally(blueprintId);
            var toSpawn = maxCount - currentCount;

            for (int i = 0; i < toSpawn; i++)
            {
                try
                {
                    // Clone the NPC
                    var npc = await Objects.CloneAsync<IMudObject>(blueprintId, this);

                    // Place in room
                    Containers.Add(roomId, npc.Id);
                    spawned++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to spawn {blueprintId} in {roomId}: {ex.Message}");
                }
            }
        }

        return spawned;
    }

    /// <summary>
    /// Count how many instances of a specific blueprint exist globally.
    /// This counts all clones regardless of where they are (room, inventory, etc.)
    /// </summary>
    private int CountSpawnsGlobally(string blueprintId)
    {
        if (Objects is null)
            return 0;
        return Objects.CountInstancesForBlueprint(blueprintId);
    }
}
