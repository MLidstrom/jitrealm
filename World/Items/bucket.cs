using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// A wooden bucket that can be filled with water from a well.
/// When filled, it can be drunk from to restore HP.
/// </summary>
public class Bucket : JitRealm.World.Std.ItemBase, IConsumable
{
    public override int Weight => 2;
    public override int Value => 3;

    /// <summary>
    /// Whether this bucket contains water.
    /// </summary>
    public bool HasWater => Ctx?.State.Get<bool>("has_water") ?? false;

    public ConsumptionType ConsumptionType => ConsumptionType.Drink;

    protected override string GetDefaultShortDescription() =>
        HasWater ? "a bucket of water" : "an empty bucket";

    public override IReadOnlyList<string> Aliases => HasWater
        ? new[] { "bucket", "water", "bucket of water", "water bucket" }
        : new[] { "bucket", "empty bucket", "wooden bucket" };

    protected override string GetDefaultDescription() => HasWater
        ? "A sturdy wooden bucket filled with cool, clear well water. The water looks refreshing."
        : "A sturdy wooden bucket with iron bands. It's empty but could hold water from a well.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "bucket");
    }

    /// <summary>
    /// Fill this bucket with water.
    /// </summary>
    public void Fill(IMudContext ctx)
    {
        ctx.State.Set("has_water", true);
    }

    /// <summary>
    /// Empty this bucket.
    /// </summary>
    public void Empty(IMudContext ctx)
    {
        ctx.State.Set("has_water", false);
    }

    public void OnUse(string userId, IMudContext ctx)
    {
        if (!HasWater)
        {
            ctx.Tell(userId, "The bucket is empty. You need to fill it with water first.");
            return;
        }

        var user = ctx.World.GetObject<ILiving>(userId);
        if (user is null)
            return;

        // Heal 10 HP from drinking water
        ctx.HealTarget(userId, 10);

        // Empty the bucket after drinking
        ctx.State.Set("has_water", false);

        // Announce consumption
        ctx.Emote("drinks deeply from the bucket of water.");
    }
}
