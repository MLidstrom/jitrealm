namespace JitRealm.Mud.AI;

/// <summary>
/// Interface for NPCs that can sell items.
/// Merchants implementing this interface will have their stock
/// included in LLM context so they can describe their wares.
/// </summary>
public interface IShopkeeper
{
    /// <summary>
    /// Items available for sale.
    /// Each entry contains the blueprint ID and the price in gold.
    /// </summary>
    IReadOnlyList<ShopItem> ShopStock { get; }

    /// <summary>
    /// Optional shop greeting shown when entering.
    /// </summary>
    string? ShopGreeting => null;
}

/// <summary>
/// An item available for sale in a shop.
/// </summary>
public sealed class ShopItem
{
    /// <summary>
    /// Display name for the item (what the shopkeeper calls it).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Blueprint ID to create when purchased (e.g., "Items/health_potion").
    /// </summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// Price in gold coins.
    /// </summary>
    public required int Price { get; init; }

    /// <summary>
    /// Optional brief description for the shopkeeper to use.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// How many are in stock (-1 = unlimited).
    /// </summary>
    public int Stock { get; init; } = -1;
}
