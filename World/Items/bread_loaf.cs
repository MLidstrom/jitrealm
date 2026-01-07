using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// A fresh loaf of bread from The Sleepy Dragon tavern.
/// Consumable - restores 8 HP when eaten.
/// </summary>
public class BreadLoaf : JitRealm.World.Std.ItemBase, IConsumable
{
    public override int Weight => 1;
    public override int Value => 3;
    public ConsumptionType ConsumptionType => ConsumptionType.Food;

    protected override string GetDefaultShortDescription() => "a loaf of bread";
    public override IReadOnlyList<string> Aliases => new[]
    {
        "bread", "loaf", "bread loaf", "food", "loaf of bread"
    };

    protected override string GetDefaultDescription() =>
        "A rustic loaf of bread with a crusty exterior and soft interior. " +
        "It's still slightly warm and smells of fresh-baked wheat.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "bread loaf");
    }

    public void OnUse(string userId, IMudContext ctx)
    {
        var user = ctx.World.GetObject<ILiving>(userId);
        if (user is null)
            return;

        // Heal 8 HP
        ctx.HealTarget(userId, 8);

        // Announce consumption
        ctx.Emote("tears off a chunk of bread and eats it.");

        // Mark as consumed (set amount to 0, driver will clean up empty items)
        ctx.State.Set("consumed", true);
        ctx.State.Set("amount", 0);
    }
}
