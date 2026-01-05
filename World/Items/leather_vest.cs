using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// A simple leather vest that can be worn for protection.
/// This serves as an example of how to create armor.
/// </summary>
public class LeatherVest : JitRealm.World.Std.ChestArmorBase
{
    public override int Weight => 3;
    public override int Value => 15;
    public override int ArmorClass => 2;
    public override string ArmorType => "leather";

    protected override string GetDefaultShortDescription() => "a worn leather vest";
    public override IReadOnlyList<string> Aliases => new[] { "vest", "leather vest", "leather armor", "armor" };
    protected override string GetDefaultDescription() =>
        "A simple leather vest, made from tanned cowhide. It's seen better days " +
        "but still offers some protection against minor attacks.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "leather vest");
    }

    public override void OnGet(IMudContext ctx, string pickerId)
    {
        ctx.Say("picks up the leather vest.");
    }

    public override void OnDrop(IMudContext ctx, string dropperId)
    {
        ctx.Say("drops the leather vest.");
    }

    public override void OnEquip(string whoId, IMudContext ctx)
    {
        ctx.Say("puts on the leather vest, fastening it snugly.");
    }

    public override void OnUnequip(string whoId, IMudContext ctx)
    {
        ctx.Say("removes the leather vest.");
    }
}
