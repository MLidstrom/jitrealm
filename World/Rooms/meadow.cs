using System;
using System.Collections.Generic;
using JitRealm.Mud;

public sealed class Meadow : MudObjectBase, IRoom
{
    // Id is assigned by the driver via MudObjectBase

    public override string Name => "A Quiet Meadow";

    public string Description => "Soft grass. The sky is ASCII-blue.";

    public IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["south"] = "Rooms/start.cs"
    };

    public IReadOnlyList<string> Contents => Array.Empty<string>();
}
