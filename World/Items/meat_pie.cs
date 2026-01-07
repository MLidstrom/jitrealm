using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// A hearty meat pie from The Sleepy Dragon tavern.
/// Consumable - restores 15 HP when eaten.
/// </summary>
public class MeatPie : JitRealm.World.Std.ItemBase, IConsumable
{
    public override int Weight => 2;
    public override int Value => 15;
    public ConsumptionType ConsumptionType => ConsumptionType.Food;

    protected override string GetDefaultShortDescription() => "a meat pie";
    public override IReadOnlyList<string> Aliases => new[]
    {
        "pie", "meat pie", "food", "pastry", "meat pastry"
    };

    protected override string GetDefaultDescription() =>
        "A golden-crusted meat pie, still warm from the oven. " +
        "The flaky pastry is stuffed with seasoned beef and vegetables, " +
        "and the aroma makes your mouth water.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "meat pie");
    }

    public void OnUse(string userId, IMudContext ctx)
    {
        var user = ctx.World.GetObject<ILiving>(userId);
        if (user is null)
            return;

        // Heal 15 HP
        ctx.HealTarget(userId, 15);

        // Announce consumption
        ctx.Emote("devours the meat pie, savoring every bite.");

        // Mark as consumed (set amount to 0, driver will clean up empty items)
        ctx.State.Set("consumed", true);
        ctx.State.Set("amount", 0);
    }
}
