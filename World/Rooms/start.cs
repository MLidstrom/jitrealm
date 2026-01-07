using System;
using System.Collections.Generic;
using JitRealm.Mud;

public sealed class StartRoom : OutdoorRoomBase, ISpawner
{
    protected override string GetDefaultName() => "A Worn Path";

    protected override string GetDefaultDescription() =>
        "A dusty path winds between rolling hills, the grass worn away by countless travelers. " +
        "To the south, the sounds of village life drift on the breeze - the faint clang of a " +
        "smithy's hammer, the murmur of voices, the creak of a waterwheel. A weathered wooden " +
        "signpost points the way to Millbrook. The path continues north toward a misty meadow.";

    public override IReadOnlyDictionary<string, string> Details => new Dictionary<string, string>
    {
        ["signpost"] = "A weathered wooden signpost with an arrow pointing south. 'Millbrook' is " +
                       "carved into the wood in rough letters, the grain darkened by years of weather.",
        ["sign"] = "A weathered wooden signpost with an arrow pointing south. 'Millbrook' is " +
                   "carved into the wood in rough letters, the grain darkened by years of weather.",
        ["path"] = "A well-worn dirt path, packed hard by countless feet over the years. " +
                   "Tufts of grass grow along the edges, but the center is bare earth.",
        ["hills"] = "Gentle rolling hills covered in wild grass and occasional wildflowers. " +
                    "They stretch off toward the horizon in every direction.",
        ["grass"] = "Wild grass waves gently in the breeze. Here and there, small wildflowers " +
                    "add spots of color - yellow buttercups and white daisies."
    };

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["north"] = "Rooms/meadow.cs",
        ["south"] = "Rooms/village_square.cs"
    };

    // ISpawner implementation - spawn the rusty sword
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["Items/rusty_sword.cs"] = 1
    };

    public void Respawn(IMudContext ctx)
    {
        // Called by the driver to replenish spawns
    }

    public override void Reset(IMudContext ctx)
    {
        // Room reset
        ctx.Say("The wind rustles through the grass.");
    }
}
