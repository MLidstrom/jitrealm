using System;
using System.Threading.Tasks;
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

    public ObjectManager? Objects { get; set; }
    public ContainerRegistry Containers { get; } = new();
    public EquipmentRegistry Equipment { get; } = new();
    public CombatScheduler Combat { get; } = new();
    public MessageQueue Messages { get; } = new();
    public HeartbeatScheduler Heartbeats { get; }
    public CallOutScheduler CallOuts { get; }

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
            // Count current spawns of this type in the room
            var currentCount = CountSpawnsInRoom(roomId, blueprintId);
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
    /// Count how many instances of a specific blueprint are in a room.
    /// </summary>
    private int CountSpawnsInRoom(string roomId, string blueprintId)
    {
        if (Objects is null)
            return 0;

        var normalizedBlueprint = blueprintId.Replace("\\", "/").ToLowerInvariant();
        if (!normalizedBlueprint.EndsWith(".cs"))
            normalizedBlueprint += ".cs";

        int count = 0;
        foreach (var objId in Containers.GetContents(roomId))
        {
            // Check if this object is a clone of the specified blueprint
            // Clone IDs look like "npcs/goblin.cs#000001"
            var normalizedObjId = objId.Replace("\\", "/").ToLowerInvariant();
            if (normalizedObjId.StartsWith(normalizedBlueprint))
            {
                count++;
            }
        }

        return count;
    }
}
