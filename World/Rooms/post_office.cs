using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Millbrook Post Office - a cramped office run by Cornelius Inksworth.
/// Contains the village notice board.
/// </summary>
public sealed class PostOffice : IndoorRoomBase, ISpawner
{
    protected override string GetDefaultName() => "Millbrook Post Office";

    protected override string GetDefaultDescription() =>
        "A cramped office with a high wooden counter separating the public area from " +
        "towering shelves of pigeonholes stuffed with letters and parcels. Dust motes " +
        "drift through a single shaft of light from a grimy window. The smell of ink, " +
        "sealing wax, and old paper permeates everything. A brass bell sits on the " +
        "counter with a sign reading 'Ring for Service.'";

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["east"] = "Rooms/village_square.cs",
    };

    public override IReadOnlyDictionary<string, string> Details => new Dictionary<string, string>
    {
        ["counter"] = "A high wooden counter worn smooth by countless elbows. A brass scale for " +
                      "weighing parcels sits at one end, alongside a stamp pad and an inkwell. " +
                      "Various official-looking forms are stacked in neat piles.",
        ["pigeonholes"] = "Rows of wooden compartments stuffed with letters, some yellowed with age. " +
                         "Each compartment is labeled with a name or address in faded ink. Some " +
                         "letters appear to have been waiting a very long time.",
        ["bell"] = "A small brass bell. The sign says 'Ring for Service' but it looks like it " +
                   "hasn't been polished in years. You suspect ringing it would bring a lengthy " +
                   "lecture about proper postal procedures.",
        ["window"] = "A grimy window letting in a single dusty shaft of light. The glass is thick " +
                     "and distorted with age, giving the view outside a watery appearance.",
        ["forms"] = "Stacks of official forms for various postal services: parcels, letters, " +
                    "registered mail, complaints, and several you don't recognize. Each form " +
                    "appears to require at least three signatures and a stamp.",
        ["letters"] = "Bundles of letters in various states - some fresh and crisp, others " +
                      "yellowed and forgotten. A few appear to be addressed to people who " +
                      "must have moved away years ago.",
    };

    /// <summary>
    /// Spawn the postmaster and notice board in this room.
    /// </summary>
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["npcs/postmaster.cs"] = 1,
        ["Items/notice_board.cs"] = 1,
    };

    public void Respawn(IMudContext ctx)
    {
        // Called by driver
    }
}
