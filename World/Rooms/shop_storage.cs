using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// The shop's storage room where inventory is kept.
/// Items are spawned here via ISpawner and are available for purchase.
/// This room is not accessible to players directly.
/// </summary>
public sealed class ShopStorage : IndoorRoomBase, ISpawner
{
    protected override string GetDefaultName() => "Shop Storage";

    protected override string GetDefaultDescription() =>
        "A cramped backroom filled with crates and shelves. " +
        "Various goods are stacked neatly, ready to be moved to the shop floor.";

    // No exits - players shouldn't be able to enter this room
    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>();

    /// <summary>
    /// Items to spawn in storage. The driver handles the actual spawning.
    /// </summary>
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["Items/health_potion.cs"] = 3,
        ["Items/rusty_sword.cs"] = 2,
        ["Items/leather_vest.cs"] = 1,
        ["Items/iron_helm.cs"] = 1,
    };

    public void Respawn(IMudContext ctx)
    {
        // Called by driver to replenish stock
    }
}
