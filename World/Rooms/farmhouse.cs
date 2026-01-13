using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Tom's farmhouse - a cozy cottage where Tom lives with his wife Martha and children.
/// Connected to the meadow where he tends his crops.
/// </summary>
public sealed class Farmhouse : IndoorRoomBase
{
    protected override string GetDefaultName() => "Tom's Farmhouse";

    /// <summary>
    /// Room aliases for location matching in NPC plans.
    /// </summary>
    public override IReadOnlyList<string> Aliases => new[]
    {
        "farmhouse", "farm house", "cottage", "home", "tom's home",
        "tom's farm", "greenfield farm", "farmer's home"
    };

    protected override string GetDefaultDescription() =>
        "A modest but well-kept farmhouse with whitewashed walls and a thatched roof. " +
        "The main room serves as kitchen, dining area, and living space all in one. " +
        "A worn wooden table sits in the center, surrounded by mismatched chairs. " +
        "The smell of bread and herbs fills the air. A stone hearth dominates one wall, " +
        "with cooking pots hanging nearby.";

    public override IReadOnlyDictionary<string, string> Details => new Dictionary<string, string>
    {
        ["table"] = "A sturdy oak table, worn smooth by years of family meals. " +
                    "A few breadcrumbs and a wooden bowl of apples sit on its surface.",
        ["hearth"] = "The stone hearth is the heart of the home. A small fire crackles, " +
                     "keeping a pot of stew warm. The mantle holds a few family keepsakes.",
        ["fire"] = "A modest fire burns in the hearth, casting warm light across the room.",
        ["chairs"] = "A collection of mismatched wooden chairs, each with its own character. " +
                     "One has a cushion, another a carved back - collected over the years.",
        ["pots"] = "Cast iron cooking pots hang from hooks near the hearth, well-seasoned from use.",
        ["roof"] = "The thatched roof is thick and well-maintained, keeping out rain and cold.",
        ["walls"] = "The whitewashed walls are clean but show the wear of a lived-in home. " +
                    "A few dried herbs hang from wooden pegs."
    };

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["east"] = "Rooms/meadow.cs"
    };
}
