using System;
using System.Collections.Generic;
using JitRealm.Mud;

public sealed class StartRoom : MudObjectBase, IRoom
{
    // Id is assigned by the driver via MudObjectBase

    public override string Name => "The Starting Room";

    public string Description => "A bare room with stone walls. A flickering terminal cursor seems to watch you.";

    public IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["north"] = "Rooms/meadow.cs"
    };

    public IReadOnlyList<string> Contents => Array.Empty<string>();
}
