using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JitRealm.Mud;

/// <summary>
/// A shop sign that dynamically lists items from the storage room.
/// Shows item names and prices (based on Value with markup).
/// </summary>
public sealed class ShopSign : SignBase
{
    /// <summary>
    /// The storage room ID to read inventory from.
    /// </summary>
    private const string StorageRoomId = "Rooms/shop_storage.cs#000001";
    private const string StorageBlueprintId = "Rooms/shop_storage";

    /// <summary>
    /// Price markup multiplier (e.g., 1.5 = 50% markup over base value).
    /// </summary>
    private const double PriceMarkup = 1.5;

    public override string Name => "a wooden sign";
    public override string ReadableLabel => "wooden sign";
    public override IReadOnlyList<string> Aliases => new[] { "sign", "wooden sign", "price list", "list", "prices" };

    public override string ReadableText
    {
        get
        {
            if (Ctx is null)
                return "The sign is blank.";

            var sb = new StringBuilder();
            sb.AppendLine("=== THE GENERAL STORE ===");
            sb.AppendLine();
            sb.AppendLine("Items for sale:");
            sb.AppendLine();

            // Try to find the storage room
            var storageContents = GetStorageContents();

            if (storageContents.Count == 0)
            {
                sb.AppendLine("  (Out of stock)");
            }
            else
            {
                // Group items by type and list with prices
                var grouped = storageContents
                    .GroupBy(item => item.ShortDescription)
                    .OrderBy(g => g.Key);

                foreach (var group in grouped)
                {
                    var first = group.First();
                    var count = group.Count();
                    var priceCopper = CalculatePriceCopper(first.Value);

                    var name = count > 1
                        ? $"{count}x {first.ShortDescription}"
                        : first.ShortDescription;

                    sb.AppendLine($"  {name,-30} {FormatPrice(priceCopper)}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Ask the shopkeeper to buy items.");

            return sb.ToString().TrimEnd();
        }
    }

    private List<IItem> GetStorageContents()
    {
        var items = new List<IItem>();
        if (Ctx is null)
            return items;

        // First try the specific instance
        var contents = Ctx.World.GetRoomContents(StorageRoomId);

        // If that's empty, try to find any storage room instance
        if (contents.Count == 0)
        {
            // Look for any instance of the storage room
            foreach (var objId in Ctx.World.ListObjectIds())
            {
                if (objId.StartsWith("Rooms/shop_storage", StringComparison.OrdinalIgnoreCase))
                {
                    contents = Ctx.World.GetRoomContents(objId);
                    if (contents.Count > 0)
                        break;
                }
            }
        }

        foreach (var objId in contents)
        {
            var item = Ctx.World.GetObject<IItem>(objId);
            if (item is not null)
            {
                items.Add(item);
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
        // Round to nearest 50 copper (0.5 SC) for cleaner prices
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
