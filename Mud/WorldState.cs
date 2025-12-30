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
}
