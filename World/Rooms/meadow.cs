using JitRealm.Mud;

public sealed class Meadow : IRoom
{
    public string Id => "Rooms/meadow.cs";
    public string Name => "A Quiet Meadow";

    public string Description => "Soft grass. The sky is ASCII-blue.";

    public IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["south"] = "Rooms/start.cs"
    };

    public IReadOnlyList<string> Contents => Array.Empty<string>();

    public void Create(WorldState state) { }
}
