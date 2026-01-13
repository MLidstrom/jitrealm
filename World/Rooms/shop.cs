using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JitRealm.Mud;

/// <summary>
/// A cozy shop where the shopkeeper sells wares.
/// Implements ISpawner to spawn the shopkeeper NPC and a price sign.
/// Implements IHasCommands to provide buy/sell commands.
/// Implements IHasLinkedRooms to load the storage room.
/// Stock is stored in the shop_storage room (hidden from players).
/// </summary>
public sealed class Shop : IndoorRoomBase, ISpawner, IHasCommands, IHasLinkedRooms
{
    protected override string GetDefaultName() => "The General Store";

    /// <summary>
    /// Room aliases for location matching in NPC plans.
    /// </summary>
    public override IReadOnlyList<string> Aliases => new[] { "shop", "store", "general store" };

    protected override string GetDefaultDescription() =>
        "A cluttered but cozy shop filled with all manner of goods. " +
        "Dusty shelves line the walls, stacked with potions, weapons, and curious trinkets. " +
        "A worn wooden counter separates the merchandise from a small backroom. " +
        "A wooden sign on the wall lists items for sale.";

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["west"] = "Rooms/village_square.cs"
    };

    /// <summary>
    /// Spawn the shopkeeper and sign in this room.
    /// </summary>
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["npcs/shopkeeper.cs"] = 1,
        ["Items/shop_sign.cs"] = 1,
    };

    /// <summary>
    /// The storage room should be loaded when the shop is active.
    /// </summary>
    public IReadOnlyList<string> LinkedRooms => new[] { "Rooms/shop_storage.cs" };

    /// <summary>
    /// Local commands available in the shop.
    /// </summary>
    public IReadOnlyList<LocalCommandInfo> LocalCommands => new LocalCommandInfo[]
    {
        new("buy", new[] { "purchase" }, "buy <item>", "Purchase an item from the shop"),
        new("sell", Array.Empty<string>(), "sell <item>", "Sell an item from your inventory"),
    };

    public async Task HandleLocalCommandAsync(string command, string[] args, string playerId, IMudContext ctx)
    {
        switch (command)
        {
            case "buy":
                await HandleBuyAsync(args, playerId, ctx);
                break;
            case "sell":
                await HandleSellAsync(args, playerId, ctx);
                break;
        }
    }

    private async Task HandleBuyAsync(string[] args, string playerId, IMudContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Tell(playerId, "Buy what? Type 'read sign' to see available items.");
            return;
        }

        var itemName = string.Join(" ", args);

        // Find storage room
        var storageId = FindStorageRoom(ctx);
        if (storageId == null)
        {
            ctx.Tell(playerId, "The shop appears to be closed.");
            return;
        }

        // Find the item in storage
        var itemId = ctx.FindItem(itemName, storageId);
        if (itemId == null)
        {
            ctx.Tell(playerId, $"We don't have '{itemName}' in stock.");
            return;
        }

        var item = ctx.World.GetObject<IItem>(itemId);
        if (item == null)
        {
            ctx.Tell(playerId, "That item seems to have vanished.");
            return;
        }

        // Calculate price in copper (Value * 1.5, rounded to nearest 5)
        var priceCopper = CalculatePriceCopper(item.Value);

        // Check player's total coin value
        var playerWealth = ctx.GetCopperValue(playerId);

        if (playerWealth < priceCopper)
        {
            ctx.Tell(playerId, $"That costs {FormatPrice(priceCopper)}. You have {FormatPrice(playerWealth)}.");
            return;
        }

        // Check if player can carry it
        var player = ctx.World.GetObject<IPlayer>(playerId);
        if (player != null && !player.CanCarry(item.Weight))
        {
            ctx.Tell(playerId, "You can't carry any more weight.");
            return;
        }

        // Deduct coins from player (with proper change)
        await ctx.DeductCoinsAsync(playerId, priceCopper);
        ctx.Move(itemId, playerId);

        ctx.Tell(playerId, $"You purchase {item.ShortDescription} for {FormatPrice(priceCopper)}.");
        ctx.Say($"Thanks for your purchase!");
    }

    private async Task HandleSellAsync(string[] args, string playerId, IMudContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Tell(playerId, "Sell what? Check your inventory with 'i'.");
            return;
        }

        var itemName = string.Join(" ", args);

        // Find the item in player's inventory
        var itemId = ctx.FindItem(itemName, playerId);
        if (itemId == null)
        {
            ctx.Tell(playerId, $"You're not carrying '{itemName}'.");
            return;
        }

        var item = ctx.World.GetObject<IItem>(itemId);
        if (item == null)
        {
            ctx.Tell(playerId, "That doesn't seem to be a valid item.");
            return;
        }

        // Don't allow selling coins
        if (item is ICoin)
        {
            ctx.Tell(playerId, "You can't sell coins. Use 'exchange' to convert between denominations.");
            return;
        }

        // Calculate sell price in copper (half of base value in silver, converted to copper)
        // Item.Value is in silver units (1 SC = 100 CC)
        var sellPriceCopper = Math.Max(50, (item.Value * 100) / 2);

        // Find storage room to move item to (or just destruct it)
        var storageId = FindStorageRoom(ctx);

        // Give coins to player (with optimal breakdown)
        await ctx.AddCoinsAsync(playerId, sellPriceCopper);

        // Move item to storage (or remove from world if no storage)
        if (storageId != null)
        {
            ctx.Move(itemId, storageId);
        }
        else
        {
            // Just remove from player - item goes into the void
            // Note: We can't destruct from world code, so we move it to the room
            ctx.Move(itemId, Id);
        }

        ctx.Tell(playerId, $"You sell {item.ShortDescription} for {FormatPrice(sellPriceCopper)}.");
        ctx.Say($"Pleasure doing business with you!");
    }

    /// <summary>
    /// Calculate buy price in copper (Value * 1.5, rounded to nearest 5 SC).
    /// Item.Value is in silver units, so we convert to copper first.
    /// </summary>
    private static int CalculatePriceCopper(int baseValueInSilver)
    {
        // Convert from silver to copper (1 SC = 100 CC)
        var baseCopper = baseValueInSilver * 100;
        // Apply 1.5x markup
        var price = (int)(baseCopper * 1.5);
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

    private string? FindStorageRoom(IMudContext ctx)
    {
        foreach (var objId in ctx.World.ListObjectIds())
        {
            if (objId.StartsWith("Rooms/shop_storage", StringComparison.OrdinalIgnoreCase))
                return objId;
        }
        return null;
    }

    public void Respawn(IMudContext ctx)
    {
        ctx.Say("The shop seems to come alive with activity.");
    }

    public override void Reset(IMudContext ctx)
    {
        ctx.Say("The shopkeeper tidies up the merchandise.");
    }
}
