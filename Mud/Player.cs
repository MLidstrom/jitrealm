namespace JitRealm.Mud;

public sealed class Player
{
    public Player(string name) => Name = name;

    public string Name { get; }
    public string? LocationId { get; set; }
}
