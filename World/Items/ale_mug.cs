using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// A frothy mug of ale from The Sleepy Dragon tavern.
/// Consumable - restores 5 HP when drunk.
/// </summary>
public class AleMug : JitRealm.World.Std.ItemBase, IUsable
{
    public override int Weight => 1;
    public override int Value => 5;

    protected override string GetDefaultShortDescription() => "a mug of ale";
    public override IReadOnlyList<string> Aliases => new[]
    {
        "ale", "mug", "ale mug", "beer", "mug of ale", "drink"
    };

    protected override string GetDefaultDescription() =>
        "A sturdy ceramic mug filled with frothy amber ale. " +
        "The foam leaves a pleasing pattern on top, and the smell is rich and malty.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "ale mug");
    }

    public void OnUse(string userId, IMudContext ctx)
    {
        var user = ctx.World.GetObject<ILiving>(userId);
        if (user is null)
            return;

        // Heal 5 HP
        ctx.HealTarget(userId, 5);

        // Announce consumption
        ctx.Emote("drains the ale mug and wipes foam from their lips.");

        // Mark as consumed (set amount to 0, driver will clean up empty items)
        ctx.State.Set("consumed", true);
        ctx.State.Set("amount", 0);
    }
}
