namespace JitRealm.Mud;

public sealed class WorldState
{
    public Player? Player { get; set; }
    public ObjectManager? Objects { get; set; }
    public ContainerRegistry Containers { get; } = new();
}
