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
                    var price = CalculatePrice(first.Value);

                    var name = count > 1
                        ? $"{count}x {first.ShortDescription}"
                        : first.ShortDescription;

                    sb.AppendLine($"  {name,-30} {price,5} gold");
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

    private static int CalculatePrice(int baseValue)
    {
        // Apply markup and round to nearest 5
        var price = (int)(baseValue * PriceMarkup);
        return ((price + 2) / 5) * 5; // Round to nearest 5
    }
}
