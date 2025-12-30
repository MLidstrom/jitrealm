using System;
using JitRealm.Mud;

/// <summary>
/// A simple goblin monster for combat testing.
/// Spawns in rooms and can be attacked by players.
/// </summary>
public sealed class Goblin : LivingBase, IOnDamage, IOnDeath, IOnEnter, IHasEquipment
{
    public override string Name => "a goblin";
    public override int MaxHP => 30;
    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(3);
    protected override int RegenAmount => 1;

    // IHasEquipment - goblin has natural "armor" and "weapon"
    public int TotalArmorClass => 1;  // Tough skin
    public (int min, int max) WeaponDamage => (2, 4);  // Claws and crude weapon

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "goblin");
    }

    public int OnDamage(int amount, string? attackerId, IMudContext ctx)
    {
        // Goblins snarl when hurt
        if (amount > 5)
        {
            ctx.Emote("snarls angrily!");
        }
        return amount;  // No damage reduction
    }

    public void OnDeath(string? killerId, IMudContext ctx)
    {
        ctx.Emote("shrieks as it falls!");

        // Schedule respawn after 60 seconds
        ctx.CallOut(nameof(Respawn), TimeSpan.FromSeconds(60));
    }

    public void OnEnter(IMudContext ctx, string whoId)
    {
        // Goblins notice when players enter
        var entering = ctx.World.GetObject<IPlayer>(whoId);
        if (entering is not null && IsAlive)
        {
            ctx.Emote("eyes the newcomer warily.");
        }
    }

    public void Respawn(IMudContext ctx)
    {
        FullHeal(ctx);
        ctx.Emote("crawls back from the shadows!");
    }
}
