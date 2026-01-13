using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Skrix - a cunning goblin scavenger who lurks in the meadow.
/// Aggressive and will attack players on sight.
/// </summary>
public sealed class Goblin : MonsterBase, IOnDamage
{
    public override string Name => "goblin";
    protected override string GetDefaultDescription() =>
        "A scrawny goblin barely three feet tall, with mottled green skin covered in old scars " +
        "and fresh scratches. One of his pointed ears is notched from an old fight, and his " +
        "yellow eyes dart nervously in all directions. A necklace of rat teeth hangs around " +
        "his scrawny neck, and he clutches a rusty dagger that's seen better days. His grin " +
        "reveals rows of needle-sharp teeth, stained from questionable meals. The other goblins " +
        "call him 'Skrix'.";
    public override int MaxHP => 30;
    public override int ExperienceValue => 30;
    public override bool IsAggressive => true;
    public override int AggroDelaySeconds => 2;
    public override int RespawnDelaySeconds => 60;

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(3);
    protected override int RegenAmount => 1;

    public override IReadOnlyList<string> Aliases => new[]
    {
        "goblin", "skrix", "gob", "greenskin"
    };

    public override int TotalArmorClass => 1;
    public override (int min, int max) WeaponDamage => (2, 4);
    public override int WanderChance => 5;

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Skrix");
    }

    public int OnDamage(int amount, string? attackerId, IMudContext ctx)
    {
        if (amount > 5)
            ctx.Emote("hisses and clutches his rat-tooth necklace!");
        return amount;
    }

    public override void OnDeath(string? killerId, IMudContext ctx)
    {
        ctx.Emote("shrieks \"Skrix's shinies!\" as he collapses!");
        ctx.CallOut(nameof(Respawn), TimeSpan.FromSeconds(RespawnDelaySeconds));
    }

    public override void Respawn(IMudContext ctx)
    {
        FullHeal(ctx);
        ctx.Emote("scurries back from the shadows, clutching his rusty dagger!");
    }

    public override void Heartbeat(IMudContext ctx)
    {
        base.Heartbeat(ctx);

        // Goblin occasionally does idle actions
        if (Random.Shared.NextDouble() < 0.03)
        {
            var actions = new[]
            {
                "sniffs the air suspiciously.",
                "scratches at his notched ear.",
                "fingers his rat-tooth necklace nervously.",
                "mutters something about 'shinies'.",
                "peers around with darting yellow eyes.",
                "tests the edge of his rusty dagger."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
