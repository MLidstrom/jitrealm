using System;
using System.Collections.Generic;
using JitRealm.Mud;

public sealed class Meadow : MudObjectBase, IRoom, IResettable, ISpawner
{
    // Id is assigned by the driver via MudObjectBase

    public override string Name => "A Quiet Meadow";

    public string Description => "Soft grass sways in a gentle breeze. The sky is a perfect ASCII-blue. " +
                                  "A goblin lurks nearby, eyeing you warily.";

    public IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["south"] = "Rooms/start.cs"
    };

    public IReadOnlyList<string> Contents => Array.Empty<string>();

    // ISpawner implementation - spawn one goblin in this room
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["npcs/goblin.cs"] = 1
    };

    public void Reset(IMudContext ctx)
    {
        ctx.Say("The meadow rustles as creatures stir.");
    }

    public void Respawn(IMudContext ctx)
    {
        // Called by the driver to replenish spawns
        // The driver handles the actual spawning logic
        ctx.Emote("Something stirs in the tall grass...");
    }
}
