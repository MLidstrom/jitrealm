using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JitRealm.Mud;

/// <summary>
/// The Sleepy Dragon Inn - a warm tavern serving food and drinks.
/// Implements ISpawner to spawn the innkeeper NPC and menu sign.
/// Implements IHasCommands to provide buy/sell commands.
/// Implements IHasLinkedRooms to load the tavern storage.
/// </summary>
public sealed class Tavern : IndoorRoomBase, ISpawner, IHasCommands, IHasLinkedRooms
{
    protected override string GetDefaultName() => "The Sleepy Dragon Inn";

    protected override string GetDefaultDescription() =>
        "A warm, low-ceilinged common room filled with the smell of roasting meat and " +
        "pipe smoke. Rough-hewn tables and chairs crowd the sawdust floor. A stone " +
        "fireplace crackles cheerfully against the far wall, above which hangs the " +
        "mounted head of a surprisingly small dragon. A polished oak bar dominates " +
        "one side of the room.";

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["north"] = "Rooms/village_square.cs",
    };

    public override IReadOnlyDictionary<string, string> Details => new Dictionary<string, string>
    {
        ["fireplace"] = "A stone fireplace with dancing flames and a well-worn hearthrug before it. " +
                        "The warmth is welcoming after a cold day outside. A brass poker and shovel " +
                        "lean against the stone surround.",
        ["fire"] = "The flames dance merrily, casting flickering shadows across the room. " +
                   "The fire is well-tended and gives off a pleasant warmth.",
        ["dragon"] = "A mounted dragon head about the size of a large dog, with dusty green scales " +
                     "and yellowed fangs. A brass plaque beneath it reads: 'Pip the Terrible - " +
                     "Vanquished by Bertram Stoutbarrel I, Year 847'. The dragon's glass eyes seem " +
                     "to follow you around the room.",
        ["bar"] = "A well-polished oak bar with brass fittings and a row of dusty bottles behind it. " +
                  "The wood is dark with age and worn smooth by countless elbows. Several stools " +
                  "line the front.",
        ["tables"] = "Scarred wooden tables bearing the marks of countless mugs and knife games. " +
                     "Each table has a few mismatched chairs around it.",
        ["bottles"] = "Rows of bottles containing liquids of various colors - ales, wines, and " +
                      "some mysterious spirits. Many are covered in a fine layer of dust.",
    };

    /// <summary>
    /// Spawn the innkeeper and menu in this room.
    /// </summary>
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["npcs/innkeeper.cs"] = 1,
        ["Items/tavern_menu.cs"] = 1,
    };

    /// <summary>
    /// The storage room should be loaded when the tavern is active.
    /// </summary>
    public IReadOnlyList<string> LinkedRooms => new[] { "Rooms/tavern_storage.cs" };

    /// <summary>
    /// Local commands available in the tavern.
    /// </summary>
    public IReadOnlyList<LocalCommandInfo> LocalCommands => new LocalCommandInfo[]
    {
        new("buy", new[] { "purchase", "order" }, "buy <item>", "Purchase food or drinks from the tavern"),
        new("sell", Array.Empty<string>(), "sell <item>", "Sell an item from your inventory"),
    };

    public Task HandleLocalCommandAsync(string command, string[] args, string playerId, IMudContext ctx)
    {
        switch (command)
        {
            case "buy":
                HandleBuy(args, playerId, ctx);
                break;
            case "sell":
                HandleSell(args, playerId, ctx);
                break;
        }
        return Task.CompletedTask;
    }

    private void HandleBuy(string[] args, string playerId, IMudContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Tell(playerId, "Buy what? Type 'read menu' to see available items.");
            return;
        }

        var itemName = string.Join(" ", args);

        // Find storage room
        var storageId = FindStorageRoom(ctx);
        if (storageId == null)
        {
            ctx.Tell(playerId, "The kitchen appears to be closed.");
            return;
        }

        // Find the item in storage
        var itemId = ctx.FindItem(itemName, storageId);
        if (itemId == null)
        {
            ctx.Tell(playerId, $"We don't have '{itemName}' available right now.");
            return;
        }

        var item = ctx.World.GetObject<IItem>(itemId);
        if (item == null)
        {
            ctx.Tell(playerId, "That item seems to have vanished.");
            return;
        }

        // Calculate price in copper (Value * 2.0 for tavern markup)
        var priceCopper = CalculatePriceCopper(item.Value);

        // Check player's total coin value
        var playerWealth = GetPlayerCopperValue(playerId, ctx);

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

        // Deduct coins from player
        DeductCoins(playerId, priceCopper, ctx);
        ctx.Move(itemId, playerId);

        ctx.Tell(playerId, $"You purchase {item.ShortDescription} for {FormatPrice(priceCopper)}.");
        ctx.Say($"Enjoy your {item.Name}!");
    }

    private void HandleSell(string[] args, string playerId, IMudContext ctx)
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

        // Calculate sell price in copper (half of base value)
        var sellPriceCopper = Math.Max(50, (item.Value * 100) / 2);

        // Find storage room to move item to
        var storageId = FindStorageRoom(ctx);

        // Give coins to player
        AddCoins(playerId, sellPriceCopper, ctx);

        // Move item to storage or room
        if (storageId != null)
        {
            ctx.Move(itemId, storageId);
        }
        else
        {
            ctx.Move(itemId, Id);
        }

        ctx.Tell(playerId, $"You sell {item.ShortDescription} for {FormatPrice(sellPriceCopper)}.");
        ctx.Say($"Thanks! I'm sure someone will enjoy that.");
    }

    /// <summary>
    /// Calculate buy price in copper. Tavern uses 2x markup.
    /// </summary>
    private static int CalculatePriceCopper(int baseValueInSilver)
    {
        var baseCopper = baseValueInSilver * 100;
        var price = (int)(baseCopper * 2.0);
        return Math.Max(50, ((price + 25) / 50) * 50);
    }

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

    private static int GetPlayerCopperValue(string playerId, IMudContext ctx)
    {
        int total = 0;
        var inventory = ctx.World.GetRoomContents(playerId);
        foreach (var itemId in inventory)
        {
            var coin = ctx.World.GetObject<ICoin>(itemId);
            if (coin != null)
            {
                total += coin.Amount * (int)coin.Material;
            }
        }
        return total;
    }

    private static void DeductCoins(string playerId, int copperAmount, IMudContext ctx)
    {
        var inventory = ctx.World.GetRoomContents(playerId);
        int goldAmt = 0, silverAmt = 0, copperAmt = 0;
        string? goldId = null, silverId = null, copperId = null;

        foreach (var itemId in inventory)
        {
            var coin = ctx.World.GetObject<ICoin>(itemId);
            if (coin != null)
            {
                switch (coin.Material)
                {
                    case CoinMaterial.Gold:
                        goldAmt = coin.Amount;
                        goldId = itemId;
                        break;
                    case CoinMaterial.Silver:
                        silverAmt = coin.Amount;
                        silverId = itemId;
                        break;
                    case CoinMaterial.Copper:
                        copperAmt = coin.Amount;
                        copperId = itemId;
                        break;
                }
            }
        }

        var totalCopper = goldAmt * 10000 + silverAmt * 100 + copperAmt;
        var remaining = totalCopper - copperAmount;

        var newGold = remaining / 10000;
        var rem = remaining % 10000;
        var newSilver = rem / 100;
        var newCopper = rem % 100;

        UpdateCoinPile(goldId, newGold, CoinMaterial.Gold, playerId, ctx);
        UpdateCoinPile(silverId, newSilver, CoinMaterial.Silver, playerId, ctx);
        UpdateCoinPile(copperId, newCopper, CoinMaterial.Copper, playerId, ctx);
    }

    private static void AddCoins(string playerId, int copperAmount, IMudContext ctx)
    {
        var gold = copperAmount / 10000;
        var remaining = copperAmount % 10000;
        var silver = remaining / 100;
        var copper = remaining % 100;

        if (gold > 0) AddCoinPile(playerId, gold, CoinMaterial.Gold, ctx);
        if (silver > 0) AddCoinPile(playerId, silver, CoinMaterial.Silver, ctx);
        if (copper > 0) AddCoinPile(playerId, copper, CoinMaterial.Copper, ctx);
    }

    private static void UpdateCoinPile(string? pileId, int newAmount, CoinMaterial material, string playerId, IMudContext ctx)
    {
        if (newAmount <= 0)
        {
            if (pileId != null)
            {
                var state = ctx.World.GetStateStore(pileId);
                state?.Set("amount", 0);
            }
        }
        else if (pileId != null)
        {
            var state = ctx.World.GetStateStore(pileId);
            state?.Set("amount", newAmount);
        }
        else if (newAmount > 0)
        {
            AddCoinPile(playerId, newAmount, material, ctx);
        }
    }

    private static void AddCoinPile(string containerId, int amount, CoinMaterial material, IMudContext ctx)
    {
        var inventory = ctx.World.GetRoomContents(containerId);
        foreach (var itemId in inventory)
        {
            var coin = ctx.World.GetObject<ICoin>(itemId);
            if (coin != null && coin.Material == material)
            {
                var state = ctx.World.GetStateStore(itemId);
                var current = state?.Get<int>("amount") ?? 0;
                state?.Set("amount", current + amount);
                return;
            }
        }
    }

    private string? FindStorageRoom(IMudContext ctx)
    {
        foreach (var objId in ctx.World.ListObjectIds())
        {
            if (objId.StartsWith("Rooms/tavern_storage", StringComparison.OrdinalIgnoreCase))
                return objId;
        }
        return null;
    }

    public void Respawn(IMudContext ctx)
    {
        // Called when stock is replenished
    }
}
