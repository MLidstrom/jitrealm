using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Wizard Mats's home room.
/// Based on World/Rooms/generic/home.cs template.
/// </summary>
public sealed class Home : IndoorRoomBase
{
    public override string Name => "Mats's Workshop";

    public override string Description =>
        "A cozy wizard's workshop filled with arcane artifacts and glowing crystals. " +
        "Shelves line the walls, packed with dusty tomes and mysterious components. " +
        "A large oak desk sits in the corner, covered with half-finished projects.";

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["out"] = "Rooms/start"
    };
}
