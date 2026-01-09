using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Millbrook Village Square - the central hub connecting all village locations.
/// Features a well, benches, and a waterwheel by the millstream.
/// </summary>
public sealed class VillageSquare : OutdoorRoomBase, ISpawner
{
    protected override string GetDefaultName() => "Millbrook Village Square";

    protected override string GetDefaultDescription() =>
        "A cobblestone square at the heart of Millbrook village. A weathered stone well " +
        "sits in the center, surrounded by wooden benches where villagers gather to gossip. " +
        "The gentle sound of the nearby millstream mingles with creaking shop signs. An old " +
        "waterwheel turns lazily where the brook passes under a small stone bridge to the east.";

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["north"] = "Rooms/start.cs",
        ["east"] = "Rooms/shop.cs",
        ["south"] = "Rooms/tavern.cs",
        ["west"] = "Rooms/post_office.cs",
        ["southwest"] = "Rooms/blacksmith.cs",
    };

    public override IReadOnlyDictionary<string, string> Details => new Dictionary<string, string>
    {
        ["well"] = "An old stone well with a wooden bucket on a rusty chain. The water looks " +
                   "clear and cold. Generations of villagers have drawn water from this well.",
        ["benches"] = "Worn wooden benches where villagers gather to gossip and rest their feet. " +
                      "The wood is smooth from years of use.",
        ["waterwheel"] = "The old waterwheel creaks rhythmically as the millstream turns it. " +
                         "It's been here longer than anyone can remember, powering the village mill.",
        ["bridge"] = "A small stone bridge arches over the millstream. The water below is crystal " +
                     "clear, and you can see small fish darting between the stones.",
        ["millstream"] = "A gentle brook that flows through the village, providing water for the " +
                         "mill and a soothing backdrop of burbling water.",
        ["signs"] = "Colorful signs point to the various establishments: a sleepy dragon for the " +
                    "tavern, an anvil for the smithy, a quill for the post office, and a coin purse " +
                    "for the general store.",
        ["cobblestones"] = "Worn cobblestones, polished smooth by countless feet over the years. " +
                           "Some are cracked with age, and grass grows between them in places.",
    };

    /// <summary>
    /// NPCs that spawn in the square - the village cat and Tom the farmer.
    /// </summary>
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["npcs/cat.cs"] = 1,
        ["npcs/villager_tom.cs"] = 1,
    };

    public void Respawn(IMudContext ctx)
    {
        // Called by driver to replenish spawns
    }
}
