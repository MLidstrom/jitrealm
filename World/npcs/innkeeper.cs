using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Bertram Stoutbarrel - a barrel-chested innkeeper with a magnificent red beard.
/// Jovial and welcoming, proud of his dragon-slaying grandfather.
/// </summary>
public sealed class Innkeeper : NPCBase
{
    public override string Name => "innkeeper";
    protected override string GetDefaultDescription() =>
        "A barrel-chested man with a magnificent red beard braided into two thick ropes. " +
        "His rolled-up sleeves reveal forearms like ham hocks, and his leather apron is " +
        "stained with years of ale and cooking grease. Despite his intimidating size, " +
        "his eyes twinkle with good humor and he moves behind the bar with surprising grace.";
    public override int MaxHP => 500;

    public override IReadOnlyList<string> Aliases => new[]
    {
        "innkeeper", "bertram", "stoutbarrel", "bertram stoutbarrel",
        "barkeep", "bartender", "publican"
    };

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(4);

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Bertram");
    }

    public override string? GetGreeting(IPlayer player) =>
        "Welcome to The Sleepy Dragon! I'm Bertram. What can I get you, friend?";

    public override void Heartbeat(IMudContext ctx)
    {
        base.Heartbeat(ctx);

        // Idle actions
        if (Random.Shared.NextDouble() < 0.05)
        {
            var actions = new[]
            {
                "polishes a mug with a well-worn cloth.",
                "wipes down the bar, humming a tavern tune.",
                "stokes the fire, sending sparks up the chimney.",
                "laughs at something only he finds funny.",
                "arranges bottles behind the bar.",
                "glances up at the mounted dragon head proudly."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
