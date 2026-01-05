using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// An iron helmet that can be worn for protection.
/// </summary>
public class IronHelm : JitRealm.World.Std.HelmetBase
{
    public override int Weight => 4;
    public override int Value => 25;
    public override int ArmorClass => 3;
    public override string ArmorType => "metal";

    protected override string GetDefaultShortDescription() => "an iron helm";
    public override IReadOnlyList<string> Aliases => new[] { "helm", "helmet", "iron helm", "iron helmet" };
    protected override string GetDefaultDescription() =>
        "A sturdy iron helmet with a rounded top and cheek guards. " +
        "It's heavy but offers good protection for the head.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "iron helm");
    }

    public override void OnEquip(string whoId, IMudContext ctx)
    {
        ctx.Say("puts on the iron helm.");
    }

    public override void OnUnequip(string whoId, IMudContext ctx)
    {
        ctx.Say("removes the iron helm.");
    }
}
