using JitRealm.Mud;

public sealed class StartRoom : IRoom
{
    public string Id => "Rooms/start.cs";
    public string Name => "The Starting Room";

    public string Description => "A bare room with stone walls. A flickering terminal cursor seems to watch you.";

    public IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["north"] = "Rooms/meadow.cs"
    };

    public IReadOnlyList<string> Contents => Array.Empty<string>();

    public void Create(WorldState state)
    {
        // Initialization hook
    }
}
