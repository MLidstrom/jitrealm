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
    /// Single-player mode player (for backward compatibility).
    /// In multi-player mode, each session has its own Player via ISession.Player.
    /// </summary>
    public Player? Player { get; set; }

    public ObjectManager? Objects { get; set; }
    public ContainerRegistry Containers { get; } = new();
    public MessageQueue Messages { get; } = new();
    public HeartbeatScheduler Heartbeats { get; }
    public CallOutScheduler CallOuts { get; }

    /// <summary>
    /// Session manager for multi-player mode.
    /// </summary>
    public SessionManager Sessions { get; } = new();
}
