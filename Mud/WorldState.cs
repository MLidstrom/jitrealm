using System;
using System.Threading.Tasks;
using JitRealm.Mud.Diagnostics;
using JitRealm.Mud.Network;

namespace JitRealm.Mud;

public sealed class WorldState
{
    private readonly IClock _clock;

    public WorldState(IClock? clock = null)
    {
        _clock = clock ?? new SystemClock();
        Heartbeats = new HeartbeatScheduler(_clock);
        CallOuts = new CallOutScheduler(_clock);
    }

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
    /// Create a MudContext for a specific object.
    /// </summary>
    /// <param name="objectId">The object ID to create the context for</param>
    /// <param name="clock">The clock to use</param>
    /// <returns>A MudContext configured for the object</returns>
    public MudContext CreateContext(string objectId, IClock clock)
    {
        return new MudContext(this, clock)
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
        return new MudContext(this, clock)
        {
            State = Objects?.GetStateStore(objectId) ?? new DictionaryStateStore(),
            CurrentObjectId = objectId,
            RoomId = roomIdOverride ?? Containers.GetContainer(objectId)
        };
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
