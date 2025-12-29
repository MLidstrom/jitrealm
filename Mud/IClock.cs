namespace JitRealm.Mud;

public interface IClock
{
    DateTimeOffset Now { get; }
}
