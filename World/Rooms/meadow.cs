using System;
using System.Collections.Generic;
using JitRealm.Mud;

public sealed class Meadow : OutdoorRoomBase, ISpawner
{
    protected override string GetDefaultName() => "A Quiet Meadow";

    protected override string GetDefaultDescription() => "Soft grass sways in a gentle breeze. The sky is a perfect ASCII-blue. " +
                                  "Wildflowers dot the meadow in patches of color.";

    public override IReadOnlyDictionary<string, string> Details => new Dictionary<string, string>
    {
        ["grass"] = "The soft green grass reaches up to your knees, swaying gently in the breeze. " +
                    "It feels cool and damp beneath your feet.",
        ["sky"] = "The sky stretches endlessly above you, a perfect shade of ASCII-blue. " +
                  "Puffy white clouds drift lazily across it.",
        ["clouds"] = "Fluffy white clouds drift slowly across the blue expanse, " +
                     "occasionally casting shadows over the meadow below.",
        ["flowers"] = "Colorful wildflowers are scattered throughout the meadow - " +
                      "bright yellows, deep purples, and soft pinks create a natural tapestry.",
        ["wildflowers"] = "Colorful wildflowers are scattered throughout the meadow - " +
                          "bright yellows, deep purples, and soft pinks create a natural tapestry.",
        ["breeze"] = "A gentle breeze carries the sweet scent of grass and flowers. " +
                     "It feels refreshing against your skin."
    };

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["south"] = "Rooms/start.cs"
    };

    // ISpawner implementation - spawn one goblin in this room
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["npcs/goblin.cs"] = 1
    };

    public override void Reset(IMudContext ctx)
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
