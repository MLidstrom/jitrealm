namespace JitRealm.Mud;

// Optional hook interfaces (lpMUD-ish). Not wired in yet.

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
