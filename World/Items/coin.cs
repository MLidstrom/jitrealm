using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// A stackable coin item that can be gold, silver, or copper.
/// Coins automatically merge when placed in the same container.
/// Exchange rates: 1 GC = 100 SC, 1 SC = 100 CC
/// </summary>
public sealed class Coin : JitRealm.World.Std.ItemBase, ICoin
{
    /// <summary>
    /// The material of this coin pile.
    /// </summary>
    public CoinMaterial Material
    {
        get
        {
            var materialStr = Ctx?.State.Get<string>("material") ?? "Gold";
            return Enum.TryParse<CoinMaterial>(materialStr, true, out var m)
                ? m : CoinMaterial.Gold;
        }
    }

    /// <summary>
    /// The number of coins in this pile.
    /// </summary>
    public int Amount => Ctx?.State.Get<int>("amount") ?? 1;

    /// <summary>
    /// Dynamic name based on material and amount.
    /// </summary>
    public override string Name => FormatName();

    /// <summary>
    /// Brief description shown in room/inventory lists.
    /// </summary>
    public override string ShortDescription => FormatName();

    /// <summary>
    /// Detailed description when examining the coins.
    /// </summary>
    protected override string GetDefaultDescription()
    {
        var mat = Material.ToString().ToLower();
        return Amount == 1
            ? $"A single shiny {mat} coin with intricate engravings."
            : $"A pile of {Amount} {mat} coins, glinting in the light.";
    }

    /// <summary>
    /// Weight: 0.01 per coin (1000 coins = 10 weight).
    /// Returns at least 1 if any coins exist.
    /// </summary>
    public override int Weight => Math.Max(1, (int)Math.Ceiling(Amount * 0.01));

    /// <summary>
    /// Value in copper (for shop calculations).
    /// </summary>
    public override int Value => Amount * (int)Material;

    /// <summary>
    /// Aliases for referring to coins by name.
    /// </summary>
    public override IReadOnlyList<string> Aliases => GetAliases();

    public override void OnLoad(IMudContext ctx)
    {
        // Call base but we'll override the name/desc defaults
        base.OnLoad(ctx);

        // Initialize coin-specific defaults if not set
        if (!ctx.State.Has("material"))
        {
            ctx.State.Set("material", "Gold");
        }
        if (!ctx.State.Has("amount"))
        {
            ctx.State.Set("amount", 1);
        }
    }

    private string FormatName()
    {
        var mat = Material.ToString().ToLower();
        return Amount == 1 ? $"1 {mat} coin" : $"{Amount} {mat} coins";
    }

    private IReadOnlyList<string> GetAliases()
    {
        var mat = Material.ToString().ToLower();
        var abbrev = Material switch
        {
            CoinMaterial.Gold => "gc",
            CoinMaterial.Silver => "sc",
            CoinMaterial.Copper => "cc",
            _ => "coin"
        };

        return new[]
        {
            $"{mat} coin",
            $"{mat} coins",
            mat,
            abbrev,
            "coin",
            "coins",
            "money"
        };
    }
}
