using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// A sturdy iron shield from Greta Ironhand's forge.
/// Provides good protection when equipped in the off-hand.
/// </summary>
public class IronShield : JitRealm.World.Std.ShieldBase
{
    public override int Weight => 8;
    public override int Value => 40;
    public override int ArmorClass => 4;
    public override string ArmorType => "metal";

    protected override string GetDefaultShortDescription() => "an iron shield";
    public override IReadOnlyList<string> Aliases => new[]
    {
        "shield", "iron shield", "buckler"
    };

    protected override string GetDefaultDescription() =>
        "A round iron shield with a sturdy leather grip on the back. " +
        "The face is slightly domed and bears the crossed-hammer emblem of the Millbrook smithy. " +
        "Dents and scratches show it's been tested in battle.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "iron shield");
    }

    public override void OnEquip(string whoId, IMudContext ctx)
    {
        ctx.Emote("straps the iron shield to their arm.");
    }

    public override void OnUnequip(string whoId, IMudContext ctx)
    {
        ctx.Emote("removes the iron shield from their arm.");
    }
}
