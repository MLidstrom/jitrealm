using System;
using System.Collections.Generic;
using JitRealm.Mud;

public sealed class Meadow : MudObjectBase, IRoom, IResettable
{
    // Id is assigned by the driver via MudObjectBase

    public override string Name => "A Quiet Meadow";

    public string Description => "Soft grass. The sky is ASCII-blue. A goblin lurks nearby.";

    public IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["south"] = "Rooms/start.cs"
    };

    public IReadOnlyList<string> Contents => Array.Empty<string>();

    public void Reset(IMudContext ctx)
    {
        ctx.Say("The meadow rustles as creatures stir.");
    }
}
