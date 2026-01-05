using System;
using System.Collections.Generic;
using JitRealm.Mud;

public sealed class StartRoom : IndoorRoomBase, ISpawner
{
    protected override string GetDefaultName() => "The Starting Room";

    protected override string GetDefaultDescription() => "A bare room with stone walls. A flickering terminal cursor seems to watch you. A cat lounges in the corner.";

    public override IReadOnlyDictionary<string, string> Details => new Dictionary<string, string>
    {
        ["walls"] = "The stone walls are rough and ancient, covered in faint moss. " +
                    "Mysterious symbols are barely visible, etched into the rock long ago.",
        ["stone"] = "The stone walls are rough and ancient, covered in faint moss. " +
                    "Mysterious symbols are barely visible, etched into the rock long ago.",
        ["cursor"] = "A glowing terminal cursor blinks steadily in the air before you: > _ " +
                     "It seems to be waiting for something... perhaps a command?",
        ["terminal"] = "A glowing terminal cursor blinks steadily in the air before you: > _ " +
                       "It seems to be waiting for something... perhaps a command?",
        ["ground"] = "The floor is made of worn flagstones, smooth from countless footsteps. " +
                     "Dust gathers in the cracks between them.",
        ["floor"] = "The floor is made of worn flagstones, smooth from countless footsteps. " +
                    "Dust gathers in the cracks between them.",
        ["symbols"] = "Strange symbols are carved into the walls - angular runes that seem to " +
                      "shimmer faintly when you look at them directly. Their meaning is unclear.",
        ["cat"] = "A small domestic cat with soft fur. It watches you with keen, intelligent eyes, " +
                  "its tail flicking lazily back and forth."
    };

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["north"] = "Rooms/meadow.cs",
        ["east"] = "Rooms/shop.cs"
    };

    // ISpawner implementation - spawn the rusty sword and a cat
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["Items/rusty_sword.cs"] = 1,
        ["npcs/cat.cs"] = 1
    };

    public void Respawn(IMudContext ctx)
    {
        // Called by the driver to replenish spawns
    }

    public override void Reset(IMudContext ctx)
    {
        // Room reset
        ctx.Say("The room shimmers briefly.");
    }
}
