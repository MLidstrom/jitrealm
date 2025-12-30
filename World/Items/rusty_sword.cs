using JitRealm.Mud;

/// <summary>
/// A basic sword weapon that can be equipped in the main hand.
/// This serves as an example of how to create weapons.
/// </summary>
public class RustySword : JitRealm.World.Std.WeaponBase
{
    public override int Weight => 5;
    public override int Value => 10;
    public override int MinDamage => 2;
    public override int MaxDamage => 6;
    public override string WeaponType => "sword";

    public override string ShortDescription => "a rusty sword";
    public override string LongDescription =>
        "An old sword, covered in rust and nicks. Despite its poor condition, " +
        "it could still be useful in a fight. The hilt is wrapped in faded leather.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "rusty sword");
    }

    public override void OnGet(IMudContext ctx, string pickerId)
    {
        ctx.Say("clanks as you pick it up.");
    }

    public override void OnDrop(IMudContext ctx, string dropperId)
    {
        ctx.Say("clatters to the ground.");
    }

    public override void OnEquip(string whoId, IMudContext ctx)
    {
        ctx.Say("grips the rusty sword despite its worn condition.");
    }

    public override void OnUnequip(string whoId, IMudContext ctx)
    {
        ctx.Say("sheathes the rusty sword.");
    }
}
