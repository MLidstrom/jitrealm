using JitRealm.Mud;

/// <summary>
/// A healing potion that can be picked up.
/// Future phases will add consumable functionality.
/// </summary>
public class HealthPotion : JitRealm.World.Std.ItemBase
{
    public override int Weight => 1;
    public override int Value => 25;

    public override string ShortDescription => "a red potion";
    public override string LongDescription =>
        "A small glass vial filled with a luminous red liquid. " +
        "The cork is sealed with wax. It appears to be a healing potion.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "health potion");
    }
}
