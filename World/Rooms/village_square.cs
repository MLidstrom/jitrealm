using System.Collections.Generic;
using System.Threading.Tasks;
using JitRealm.Mud;

/// <summary>
/// Millbrook Village Square - the central hub connecting all village locations.
/// Features a well, benches, and a waterwheel by the millstream.
/// </summary>
public sealed class VillageSquare : OutdoorRoomBase, ISpawner, IHasCommands
{
    protected override string GetDefaultName() => "Millbrook Village Square";

    /// <summary>
    /// Room aliases for location matching in NPC plans.
    /// </summary>
    public override IReadOnlyList<string> Aliases => new[] { "square", "village", "villagers", "plaza", "millbrook", "well", "water" };

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
    /// NPCs and items that spawn in the square.
    /// </summary>
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["npcs/cat.cs"] = 1,
        ["npcs/villager_tom.cs"] = 1,
        ["Items/bucket.cs"] = 1,
    };

    public void Respawn(IMudContext ctx)
    {
        // Called by driver to replenish spawns
    }

    // IHasCommands implementation - draw water from the well
    public IReadOnlyList<LocalCommandInfo> LocalCommands => new LocalCommandInfo[]
    {
        new("draw", new[] { "fill" }, "draw water", "Draw water from the well into a bucket"),
    };

    public Task HandleLocalCommandAsync(string command, string[] args, string playerId, IMudContext ctx)
    {
        switch (command)
        {
            case "draw":
            case "fill":
                HandleDrawWater(playerId, ctx);
                break;
        }
        return Task.CompletedTask;
    }

    private void HandleDrawWater(string playerId, IMudContext ctx)
    {
        // Find a bucket in the player's inventory
        var bucketId = ctx.FindItem("bucket", playerId);
        if (bucketId is null)
        {
            ctx.Tell(playerId, "You need to be holding a bucket to draw water from the well.");
            return;
        }

        // Check if it's actually a bucket
        var bucket = ctx.World.GetObject<IMudObject>(bucketId);
        if (bucket is null || !bucket.Id.Contains("bucket"))
        {
            ctx.Tell(playerId, "You need a bucket to draw water.");
            return;
        }

        // Get the bucket's state and fill it
        var bucketState = ctx.World.GetStateStore(bucketId);
        if (bucketState is null)
        {
            ctx.Tell(playerId, "Something went wrong with the bucket.");
            return;
        }

        // Check if already full
        if (bucketState.Get<bool>("has_water"))
        {
            ctx.Tell(playerId, "The bucket is already full of water.");
            return;
        }

        // Fill the bucket
        bucketState.Set("has_water", true);
        ctx.Tell(playerId, "You lower the bucket into the well and draw up cool, clear water.");
        ctx.Emote("draws water from the well.");
    }
}
