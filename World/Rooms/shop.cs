using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// A cozy shop where the shopkeeper sells wares.
/// Implements ISpawner to spawn the shopkeeper NPC.
/// </summary>
public sealed class Shop : MudObjectBase, IRoom, IResettable, ISpawner
{
    public override string Name => "The General Store";

    public string Description =>
        "A cluttered but cozy shop filled with all manner of goods. " +
        "Dusty shelves line the walls, stacked with potions, weapons, and curious trinkets. " +
        "A worn wooden counter separates the merchandise from a small backroom.";

    public IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["west"] = "Rooms/start.cs"
    };

    public IReadOnlyList<string> Contents => Array.Empty<string>();

    /// <summary>
    /// Spawn the shopkeeper in this room.
    /// </summary>
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["npcs/shopkeeper.cs"] = 1
    };

    public void Respawn(IMudContext ctx)
    {
        ctx.Say("The shop seems to come alive with activity.");
    }

    public void Reset(IMudContext ctx)
    {
        ctx.Say("The shopkeeper tidies up the merchandise.");
    }
}
