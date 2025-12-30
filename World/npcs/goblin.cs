using System;
using JitRealm.Mud;

/// <summary>
/// A goblin monster that lurks in dark places.
/// Aggressive and will attack players on sight.
/// </summary>
public sealed class Goblin : MonsterBase, IOnDamage
{
    public override string Name => "a goblin";
    public override int MaxHP => 30;
    public override int ExperienceValue => 30;
    public override bool IsAggressive => true;
    public override int AggroDelaySeconds => 2;
    public override int RespawnDelaySeconds => 60;

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(3);
    protected override int RegenAmount => 1;

    // Natural armor (tough skin) and weapons (claws)
    public override int TotalArmorClass => 1;
    public override (int min, int max) WeaponDamage => (2, 4);

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "goblin");
    }

    public int OnDamage(int amount, string? attackerId, IMudContext ctx)
    {
        // Goblins snarl when hurt badly
        if (amount > 5)
        {
            ctx.Emote("snarls angrily!");
        }
        return amount;  // No damage reduction
    }

    public override void OnDeath(string? killerId, IMudContext ctx)
    {
        ctx.Emote("shrieks as it falls!");

        // Schedule respawn
        ctx.CallOut(nameof(Respawn), TimeSpan.FromSeconds(RespawnDelaySeconds));
    }

    public override void Respawn(IMudContext ctx)
    {
        FullHeal(ctx);
        ctx.Emote("crawls back from the shadows!");
    }
}
