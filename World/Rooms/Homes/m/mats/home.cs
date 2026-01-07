using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Personal home for wizard Mats.
/// </summary>
public sealed class Home : IndoorRoomBase
{
    protected override string GetDefaultName() => "Mats's Workshop";

    protected override string GetDefaultDescription() =>
        "A cozy workshop filled with the tools of world creation. " +
        "Maps and diagrams cover the walls, showing realms both existing and imagined. " +
        "A sturdy oak desk sits by a window overlooking the village square below, " +
        "its surface cluttered with notes, quills, and half-finished blueprints. " +
        "The smell of ink and parchment fills the air.";

    public override IReadOnlyDictionary<string, string> Details => new Dictionary<string, string>
    {
        ["desk"] = "A sturdy oak desk scarred by years of use. Papers and blueprints are scattered across its surface, held down by various curiosities.",
        ["maps"] = "Detailed maps showing the village of Millbrook and the surrounding wilderness. Some show places that don't seem to exist yet.",
        ["window"] = "A large window overlooking the village square. You can see the well and the comings and goings of villagers below.",
        ["blueprints"] = "Half-finished designs for rooms, creatures, and items. The handwriting is precise but hurried.",
    };

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["out"] = "Rooms/village_square.cs"
    };
}
