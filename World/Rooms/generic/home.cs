using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Generic wizard home room template.
/// Copy this file to World/Rooms/Homes/{letter}/{name}/home.cs for new wizards.
/// Customize the Name and Description for each wizard.
/// </summary>
public sealed class Home : IndoorRoomBase
{
    public override string Name => "Wizard's Home";

    public override string Description =>
        "A personal sanctuary for a wizard. " +
        "The room is sparsely furnished but comfortable, with a simple desk and chair. " +
        "Shelves await books and artifacts yet to be collected.";

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["out"] = "Rooms/start"
    };
}
