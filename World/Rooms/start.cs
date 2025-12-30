using System;
using System.Collections.Generic;
using JitRealm.Mud;

public sealed class StartRoom : MudObjectBase, IRoom, IResettable
{
    // Id is assigned by the driver via MudObjectBase

    public override string Name => "The Starting Room";

    public string Description => "A bare room with stone walls. A flickering terminal cursor seems to watch you. " +
        "A rusty sword lies on the ground.";

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
                      "shimmer faintly when you look at them directly. Their meaning is unclear."
    };

    public IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["north"] = "Rooms/meadow.cs",
        ["east"] = "Rooms/shop.cs"
    };

    public IReadOnlyList<string> Contents => Array.Empty<string>();

    public void Reset(IMudContext ctx)
    {
        // Room reset - could respawn items here in the future
        ctx.Say("The room shimmers briefly.");
    }
}
