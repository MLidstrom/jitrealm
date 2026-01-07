using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// A healing potion that restores 25 HP when drunk.
/// </summary>
public class HealthPotion : JitRealm.World.Std.ItemBase, IConsumable
{
    public override int Weight => 1;
    public override int Value => 25;
    public ConsumptionType ConsumptionType => ConsumptionType.Drink;

    protected override string GetDefaultShortDescription() => "a red potion";
    public override IReadOnlyList<string> Aliases => new[] { "potion", "red potion", "health potion", "vial", "healing potion" };
    protected override string GetDefaultDescription() =>
        "A small glass vial filled with a luminous red liquid. " +
        "The cork is sealed with wax. It appears to be a healing potion.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "health potion");
    }

    public void OnUse(string userId, IMudContext ctx)
    {
        var user = ctx.World.GetObject<ILiving>(userId);
        if (user is null)
            return;

        // Heal 25 HP
        ctx.HealTarget(userId, 25);

        // Announce consumption
        ctx.Emote("drinks the red potion and feels warmth spread through their body.");
    }
}
