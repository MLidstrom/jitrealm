using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// The Sleepy Dragon's cellar where food and drinks are stored.
/// Items are spawned here via ISpawner and are available for purchase.
/// This room is not accessible to players directly.
/// </summary>
public sealed class TavernStorage : IndoorRoomBase, ISpawner
{
    protected override string GetDefaultName() => "Tavern Cellar";

    protected override string GetDefaultDescription() =>
        "A cool, dark cellar beneath the tavern. Barrels of ale line the walls " +
        "and shelves hold preserved foods. The smell of yeast and smoked meat fills the air.";

    // No exits - players shouldn't be able to enter this room
    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>();

    /// <summary>
    /// Items to spawn in storage. The driver handles the actual spawning.
    /// </summary>
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["Items/ale_mug.cs"] = 5,
        ["Items/bread_loaf.cs"] = 5,
        ["Items/meat_pie.cs"] = 3,
    };

    public void Respawn(IMudContext ctx)
    {
        // Called by driver to replenish stock
    }
}
