using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Whiskers - a scruffy tabby street cat who wanders around town.
/// A friendly stray who has been prowling these streets for years.
/// </summary>
public sealed class Cat : MonsterBase, IOnDamage
{
    public override string Name => "cat";
    protected override string GetDefaultDescription() =>
        "A scruffy orange tabby cat with battle-scarred ears and a distinctive white patch on " +
        "its chest. Despite its rough appearance, its amber eyes gleam with intelligence and " +
        "a hint of mischief. The locals call this particular stray 'Whiskers' - it's been " +
        "prowling these streets for as long as anyone can remember, always turning up where " +
        "there's food to be had or a warm spot to claim.";
    public override int MaxHP => 10;
    public override int ExperienceValue => 5;
    public override bool IsAggressive => false;
    public override int AggroDelaySeconds => 5;
    public override int RespawnDelaySeconds => 120;

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(5);
    protected override int RegenAmount => 1;

    public override int TotalArmorClass => 0;
    public override (int min, int max) WeaponDamage => (1, 2);
    public override int WanderChance => 10;

    public override IReadOnlyList<string> Aliases => new[]
    {
        "cat", "whiskers", "tabby", "kitty", "stray"
    };

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Whiskers");
    }

    public int OnDamage(int amount, string? attackerId, IMudContext ctx)
    {
        ctx.Emote("yowls in pain and hisses!");
        return amount;
    }

    public override void OnDeath(string? killerId, IMudContext ctx)
    {
        ctx.Emote("lets out a final pitiful mew and goes limp.");
        ctx.CallOut(nameof(Respawn), TimeSpan.FromSeconds(RespawnDelaySeconds));
    }

    public override void Respawn(IMudContext ctx)
    {
        FullHeal(ctx);
        ctx.Emote("stretches and yawns, fully recovered.");
    }

    public override void Heartbeat(IMudContext ctx)
    {
        base.Heartbeat(ctx);

        // Cat occasionally does idle actions
        if (Random.Shared.NextDouble() < 0.03)
        {
            var actions = new[]
            {
                "purrs contentedly.",
                "stretches luxuriously.",
                "grooms its fur with careful attention.",
                "flicks its tail lazily.",
                "yawns widely, showing tiny teeth.",
                "watches a passing bug with intense focus."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
