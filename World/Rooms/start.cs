using System;
using System.Collections.Generic;
using JitRealm.Mud;

public sealed class StartRoom : MudObjectBase, IRoom, IResettable
{
    // Id is assigned by the driver via MudObjectBase

    public override string Name => "The Starting Room";

    public string Description => "A bare room with stone walls. A flickering terminal cursor seems to watch you. " +
        "A rusty sword lies on the ground.";

    public IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["north"] = "Rooms/meadow.cs"
    };

    public IReadOnlyList<string> Contents => Array.Empty<string>();

    public void Reset(IMudContext ctx)
    {
        // Room reset - could respawn items here in the future
        ctx.Say("The room shimmers briefly.");
    }
}
