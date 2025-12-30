using System;
using JitRealm.Mud;

/// <summary>
/// A friendly shopkeeper who greets visitors.
/// Can be extended to support buy/sell commands in the future.
/// </summary>
public sealed class Shopkeeper : NPCBase
{
    public override string Name => "the shopkeeper";
    public override int MaxHP => 500;

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "shopkeeper");
    }

    public override string? GetGreeting(IPlayer player)
    {
        return $"Welcome, traveler! Browse my wares if you wish.";
    }

    public override void OnEnter(IMudContext ctx, string whoId)
    {
        base.OnEnter(ctx, whoId);

        // Extra interaction: shopkeeper notices returning customers
        var entering = ctx.World.GetObject<IPlayer>(whoId);
        if (entering is not null)
        {
            // Could track visit count in state for personalized greetings
        }
    }

    public override void Heartbeat(IMudContext ctx)
    {
        base.Heartbeat(ctx);

        // Shopkeeper occasionally does idle actions
        if (Random.Shared.NextDouble() < 0.05)  // 5% chance per heartbeat
        {
            var actions = new[]
            {
                "polishes a dusty bottle.",
                "counts some coins.",
                "arranges items on a shelf.",
                "hums a quiet tune."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
