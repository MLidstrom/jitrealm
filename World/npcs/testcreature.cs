using System;
using JitRealm.Mud;

/// <summary>
/// A simple test creature to verify the Living Foundation (Phase 8).
/// Clone it, damage it, heal it, watch it regenerate.
/// </summary>
public sealed class TestCreature : LivingBase, IOnDamage, IOnDeath, IOnHeal
{
    public override string Name => "Test Creature";
    protected override string GetDefaultDescription() => "A small, translucent creature that shimmers with an otherworldly glow. It seems to exist for the sole purpose of being poked, damaged, and healed.";
    public override int MaxHP => 50;
    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(3);
    protected override int RegenAmount => 2;

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.Emote("appears with a flash!");
    }

    public int OnDamage(int amount, string? attackerId, IMudContext ctx)
    {
        ctx.Say($"Ouch! Taking {amount} damage!");
        return amount; // No damage reduction
    }

    public void OnDeath(string? killerId, IMudContext ctx)
    {
        ctx.Say("I have been defeated!");
        // Schedule respawn after 10 seconds
        ctx.CallOut(nameof(Respawn), TimeSpan.FromSeconds(10));
    }

    public void OnHeal(int amount, IMudContext ctx)
    {
        ctx.Say($"Ahh, healed for {amount}. Now at {HP}/{MaxHP} HP.");
    }

    public void Respawn(IMudContext ctx)
    {
        FullHeal(ctx);
        ctx.Emote("respawns with full health!");
    }

    /// <summary>
    /// Command handler for "poke" - deals 10 damage to self for testing.
    /// </summary>
    public void Poke(IMudContext ctx)
    {
        ctx.Say("You poked me!");
        TakeDamage(10, null, ctx);
        ctx.Say($"HP is now {HP}/{MaxHP}");
    }
}
