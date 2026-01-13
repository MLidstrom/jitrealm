using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Greta Ironhand - a powerfully built master blacksmith.
/// Gruff, no-nonsense, takes immense pride in her craft.
/// </summary>
public sealed class BlacksmithNpc : NPCBase
{
    public override string Name => "blacksmith";
    protected override string GetDefaultDescription() =>
        "A powerfully built woman in her forties with arms like tree trunks and hands " +
        "calloused from decades at the forge. Her short-cropped grey hair is singed at the tips, " +
        "and soot permanently darkens the creases around her keen brown eyes. A leather apron " +
        "covers her simple work clothes, and she moves with the confident economy of a master craftsman.";
    public override int MaxHP => 600;

    public override IReadOnlyList<string> Aliases => new[]
    {
        "blacksmith", "greta", "ironhand", "greta ironhand",
        "smith", "smithy"
    };

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(4);

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Greta");
    }

    public override string? GetGreeting(IPlayer player) =>
        "*looks up from the anvil* Need something forged? Or buying?";

    public override void Heartbeat(IMudContext ctx)
    {
        base.Heartbeat(ctx);

        // Idle actions
        if (Random.Shared.NextDouble() < 0.05)
        {
            var actions = new[]
            {
                "hammers a glowing piece of metal into shape.",
                "examines a blade, running her thumb along the edge.",
                "pumps the bellows, making the coals flare brighter.",
                "wipes soot from her face with a leather-gloved hand.",
                "inspects a finished piece with a critical eye.",
                "dunks a hot blade into the quenching barrel with a hiss.",
                "tests the weight of a newly forged sword."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
