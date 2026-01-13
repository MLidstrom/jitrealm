using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Cornelius Inksworth - a fussy, bureaucratic postmaster.
/// Thin, stooped, with ink-stained fingers and wire-rimmed spectacles.
/// </summary>
public sealed class Postmaster : NPCBase
{
    public override string Name => "postmaster";
    protected override string GetDefaultDescription() =>
        "A thin, stooped man with wire-rimmed spectacles perched on a beak-like nose. " +
        "His fingers are perpetually stained with ink, and he wears a green eyeshade that " +
        "casts his face in shadow. He peers at everything with deep suspicion, as if expecting " +
        "the world to try and send packages without proper postage.";
    public override int MaxHP => 300;

    public override IReadOnlyList<string> Aliases => new[]
    {
        "postmaster", "cornelius", "inksworth", "cornelius inksworth",
        "clerk", "postal clerk"
    };

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(2);

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Cornelius");
    }

    public override string? GetGreeting(IPlayer player) =>
        "*peers over spectacles* Yes? State your business. The post office closes at sundown, you know.";

    public override void Heartbeat(IMudContext ctx)
    {
        base.Heartbeat(ctx);

        // Idle actions
        if (Random.Shared.NextDouble() < 0.05)
        {
            var actions = new[]
            {
                "stamps a document with unnecessary force.",
                "sorts through a stack of yellowed letters.",
                "adjusts his spectacles and squints at something.",
                "mutters about improper postage.",
                "carefully aligns a stack of papers.",
                "sighs heavily at nothing in particular.",
                "peers suspiciously at a parcel."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
