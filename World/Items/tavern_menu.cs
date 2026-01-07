using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JitRealm.Mud;

/// <summary>
/// A menu board in The Sleepy Dragon tavern.
/// Dynamically displays food and drink items from the tavern storage.
/// </summary>
public sealed class TavernMenu : SignBase
{
    /// <summary>
    /// Price markup multiplier (taverns have higher markup than general store).
    /// </summary>
    private const double PriceMarkup = 2.0;

    public override string Name => "a menu board";
    public override string ReadableLabel => "menu board";
    public override IReadOnlyList<string> Aliases => new[]
    {
        "menu", "menu board", "board", "prices", "food menu", "drink menu"
    };

    public override string ReadableText
    {
        get
        {
            if (Ctx is null)
                return "The menu is blank.";

            var sb = new StringBuilder();
            sb.AppendLine("=== THE SLEEPY DRAGON ===");
            sb.AppendLine("     Est. Year 847");
            sb.AppendLine();
            sb.AppendLine("FOOD & DRINK:");
            sb.AppendLine();

            var storageContents = GetStorageContents();

            if (storageContents.Count == 0)
            {
                sb.AppendLine("  (Kitchen is closed)");
            }
            else
            {
                // Group items by type and list with prices
                var grouped = storageContents
                    .GroupBy(item => item.ShortDescription)
                    .OrderByDescending(g => g.First().Value) // Most expensive first
                    .ThenBy(g => g.Key);

                foreach (var group in grouped)
                {
                    var first = group.First();
                    var priceCopper = CalculatePriceCopper(first.Value);
                    var name = first.ShortDescription;

                    sb.AppendLine($"  {name,-25} {FormatPrice(priceCopper)}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Ask the innkeeper to purchase.");
            sb.AppendLine();
            sb.AppendLine("  \"Good food, cold ale, warm beds\"");

            return sb.ToString().TrimEnd();
        }
    }

    private List<IItem> GetStorageContents()
    {
        var items = new List<IItem>();
        if (Ctx is null)
            return items;

        // Look for any tavern storage room instance
        foreach (var objId in Ctx.World.ListObjectIds())
        {
            if (objId.StartsWith("Rooms/tavern_storage", StringComparison.OrdinalIgnoreCase))
            {
                var contents = Ctx.World.GetRoomContents(objId);
                foreach (var itemId in contents)
                {
                    var item = Ctx.World.GetObject<IItem>(itemId);
                    if (item is not null)
                    {
                        items.Add(item);
                    }
                }
                if (items.Count > 0)
                    break;
            }
        }

        return items;
    }

    /// <summary>
    /// Calculate buy price in copper. Item.Value is in silver units.
    /// </summary>
    private static int CalculatePriceCopper(int baseValueInSilver)
    {
        // Convert from silver to copper (1 SC = 100 CC)
        var baseCopper = baseValueInSilver * 100;
        // Apply markup
        var price = (int)(baseCopper * PriceMarkup);
        // Round to nearest 50 copper for cleaner prices
        return Math.Max(50, ((price + 25) / 50) * 50);
    }

    /// <summary>
    /// Format a copper value as breakdown (e.g., "1 GC, 50 SC").
    /// </summary>
    private static string FormatPrice(int copperAmount)
    {
        if (copperAmount <= 0)
            return "0 CC";

        var gold = copperAmount / 10000;
        var remaining = copperAmount % 10000;
        var silver = remaining / 100;
        var copper = remaining % 100;

        var parts = new List<string>();
        if (gold > 0) parts.Add($"{gold} GC");
        if (silver > 0) parts.Add($"{silver} SC");
        if (copper > 0) parts.Add($"{copper} CC");

        return parts.Count > 0 ? string.Join(", ", parts) : "0 CC";
    }
}
