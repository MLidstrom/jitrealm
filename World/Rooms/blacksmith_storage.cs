using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// The blacksmith's stockroom where finished goods await sale.
/// Items are spawned here via ISpawner and are available for purchase.
/// This room is not accessible to players directly.
/// </summary>
public sealed class BlacksmithStorage : IndoorRoomBase, ISpawner
{
    protected override string GetDefaultName() => "Smithy Stockroom";

    protected override string GetDefaultDescription() =>
        "A small back room filled with finished goods awaiting sale. " +
        "Weapons hang on wall racks and armor sits on wooden stands, " +
        "each piece gleaming with fresh oil.";

    // No exits - players shouldn't be able to enter this room
    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>();

    /// <summary>
    /// Items to spawn in storage. The driver handles the actual spawning.
    /// </summary>
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["Items/iron_sword.cs"] = 2,
        ["Items/iron_shield.cs"] = 1,
        ["Items/iron_helm.cs"] = 1,
        ["Items/leather_vest.cs"] = 1,
    };

    public void Respawn(IMudContext ctx)
    {
        // Called by driver to replenish stock
    }
}
