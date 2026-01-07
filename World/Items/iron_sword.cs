using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// A quality iron sword from Greta Ironhand's forge.
/// Better damage than the rusty sword - a reliable weapon.
/// </summary>
public class IronSword : JitRealm.World.Std.WeaponBase
{
    public override int Weight => 5;
    public override int Value => 50;
    public override int MinDamage => 4;
    public override int MaxDamage => 10;
    public override string WeaponType => "sword";

    protected override string GetDefaultShortDescription() => "an iron sword";
    public override IReadOnlyList<string> Aliases => new[]
    {
        "sword", "iron sword", "blade", "weapon"
    };

    protected override string GetDefaultDescription() =>
        "A well-crafted iron sword with a gleaming blade and leather-wrapped grip. " +
        "The edge is sharp and the balance is excellent - clearly the work of a skilled smith. " +
        "A small maker's mark near the hilt reads 'G.I.' for Greta Ironhand.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "iron sword");
    }

    public override void OnGet(IMudContext ctx, string pickerId)
    {
        ctx.Emote("rings softly as you pick it up.");
    }

    public override void OnDrop(IMudContext ctx, string dropperId)
    {
        ctx.Emote("clatters to the ground with a metallic ring.");
    }

    public override void OnEquip(string whoId, IMudContext ctx)
    {
        ctx.Emote("draws the iron sword, testing its balance.");
    }

    public override void OnUnequip(string whoId, IMudContext ctx)
    {
        ctx.Emote("sheathes the iron sword.");
    }
}
