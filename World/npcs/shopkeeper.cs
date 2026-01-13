using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Barnaby Thimblewick - a friendly shopkeeper who greets visitors.
/// Stock is stored in the shop_storage room; a sign in the shop lists prices.
/// </summary>
public sealed class Shopkeeper : NPCBase
{
    public override string Name => "shopkeeper";
    protected override string GetDefaultDescription() =>
        "A stout, balding man in his late fifties with rosy cheeks and twinkling blue eyes behind " +
        "small round spectacles. His leather apron is well-worn but clean, and his thick fingers " +
        "are surprisingly nimble from years of counting coins and wrapping packages. A magnificent " +
        "grey mustache, waxed to perfect curls at the tips, gives him a distinguished air despite " +
        "the flour dust perpetually dusting his shoulders. A small brass nameplate on his apron " +
        "reads 'Barnaby Thimblewick'.";
    public override int MaxHP => 500;

    public override IReadOnlyList<string> Aliases => new[]
    {
        "shopkeeper", "barnaby", "thimblewick", "barnaby thimblewick",
        "keeper", "merchant", "shop keeper", "store keeper"
    };

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(4);

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Barnaby");
    }

    public override string? GetGreeting(IPlayer player) =>
        "Ah, welcome, welcome! I'm Barnaby Thimblewick. Browse my wares if you wish, friend!";

    public override void Heartbeat(IMudContext ctx)
    {
        base.Heartbeat(ctx);

        // Barnaby occasionally does idle actions
        if (Random.Shared.NextDouble() < 0.05)
        {
            var actions = new[]
            {
                "polishes a dusty bottle with his apron.",
                "counts some coins, muttering softly.",
                "adjusts his spectacles and peers at a shelf.",
                "strokes his magnificent mustache thoughtfully.",
                "hums a cheerful tune while organizing wares.",
                "examines an old coin from his collection."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
