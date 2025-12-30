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
    public MessageQueue Messages { get; } = new();
    public HeartbeatScheduler Heartbeats { get; }
    public CallOutScheduler CallOuts { get; }

    /// <summary>
    /// Session manager for multi-player mode.
    /// Each session has a PlayerId pointing to a cloned player world object.
    /// Player location is tracked via ContainerRegistry.
    /// </summary>
    public SessionManager Sessions { get; } = new();
}
