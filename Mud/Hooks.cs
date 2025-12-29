namespace JitRealm.Mud;

// Optional hook interfaces (lpMUD-ish).

public interface IOnLoad
{
    void OnLoad(IMudContext ctx);
}

public interface IOnEnter
{
    void OnEnter(IMudContext ctx, string whoId);
}

public interface IOnLeave
{
    void OnLeave(IMudContext ctx, string whoId);
}

public interface IHeartbeat
{
    TimeSpan HeartbeatInterval { get; }
    void Heartbeat(IMudContext ctx);
}

public interface IResettable
{
    void Reset(IMudContext ctx);
}

public interface IOnReload
{
    /// <summary>
    /// Called after a blueprint reload, before the old instance is discarded.
    /// Allows custom state migration or reinitialization logic.
    /// </summary>
    /// <param name="ctx">Context with preserved state store</param>
    /// <param name="oldTypeName">Fully qualified name of the previous type</param>
    void OnReload(IMudContext ctx, string oldTypeName);
}
