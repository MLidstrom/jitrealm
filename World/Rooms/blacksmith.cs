using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JitRealm.Mud;

/// <summary>
/// Millbrook Smithy - Greta Ironhand's forge selling weapons and armor.
/// Implements ISpawner to spawn the blacksmith NPC.
/// Implements IHasCommands to provide buy/sell commands.
/// Implements IHasLinkedRooms to load the blacksmith storage.
/// </summary>
public sealed class Blacksmith : IndoorRoomBase, ISpawner, IHasCommands, IHasLinkedRooms
{
    protected override string GetDefaultName() => "Millbrook Smithy";

    protected override string GetDefaultDescription() =>
        "The heat hits you like a wall as you enter the forge. A massive stone hearth " +
        "dominates the room, its coals glowing cherry-red. Hammers, tongs, and half-finished " +
        "blades hang from hooks along soot-blackened walls. The rhythmic clang of metal on " +
        "metal fills the air, and the smell of hot iron and coal smoke is almost overwhelming.";

    public override IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["northeast"] = "Rooms/village_square.cs",
    };

    public override IReadOnlyDictionary<string, string> Details => new Dictionary<string, string>
    {
        ["hearth"] = "A massive stone hearth filled with glowing coals. The heat radiating from it " +
                     "is intense, and the air above it shimmers. Large bellows stand ready to fan " +
                     "the flames hotter.",
        ["forge"] = "A massive stone hearth filled with glowing coals. The heat radiating from it " +
                    "is intense. This is where raw iron becomes steel, and steel becomes weapons.",
        ["coals"] = "Cherry-red coals glow intensely in the hearth. The heat is almost unbearable " +
                    "standing this close.",
        ["anvil"] = "A heavy iron anvil scarred by countless hammer strikes. It's clearly seen years " +
                    "of hard use, and the surface is pitted and marked with the ghosts of a thousand " +
                    "blades.",
        ["tools"] = "An impressive array of blacksmithing tools: hammers of various sizes, tongs, " +
                    "files, punches, and other implements of the trade. Each tool is well-worn but " +
                    "carefully maintained.",
        ["hammers"] = "Hammers of various sizes hang from pegs on the wall - from small ball-peins " +
                      "to massive sledges. Each is polished from years of use.",
        ["weapons"] = "Finished weapons hang on display: swords, axes, and daggers, each gleaming " +
                      "with fresh oil. A small tag on each shows the price.",
        ["armor"] = "Several pieces of armor sit on wooden stands: helms, shields, and vests. " +
                    "Each piece is stamped with Greta's maker's mark - crossed hammers.",
        ["bellows"] = "Large leather bellows connected to the forge. When pumped, they make the " +
                      "coals flare to white-hot intensity.",
    };

    /// <summary>
    /// Spawn the blacksmith NPC in this room.
    /// </summary>
    public IReadOnlyDictionary<string, int> Spawns => new Dictionary<string, int>
    {
        ["npcs/blacksmith.cs"] = 1,
    };

    /// <summary>
    /// The storage room should be loaded when the smithy is active.
    /// </summary>
    public IReadOnlyList<string> LinkedRooms => new[] { "Rooms/blacksmith_storage.cs" };

    /// <summary>
    /// Local commands available in the smithy.
    /// </summary>
    public IReadOnlyList<LocalCommandInfo> LocalCommands => new LocalCommandInfo[]
    {
        new("buy", new[] { "purchase" }, "buy <item>", "Purchase weapons or armor from the smithy"),
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
            ctx.Tell(playerId, "Buy what? Look around to see what's available.");
            return;
        }

        var itemName = string.Join(" ", args);

        // Find storage room
        var storageId = FindStorageRoom(ctx);
        if (storageId == null)
        {
            ctx.Tell(playerId, "The smithy appears to be out of stock.");
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

        // Calculate price in copper (Value * 1.5 for smithy markup)
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
        ctx.Say($"Good steel. Take care of it.");
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
        ctx.Say($"*grunts* Fair price.");
    }

    /// <summary>
    /// Calculate buy price in copper. Smithy uses 1.5x markup.
    /// </summary>
    private static int CalculatePriceCopper(int baseValueInSilver)
    {
        var baseCopper = baseValueInSilver * 100;
        var price = (int)(baseCopper * 1.5);
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
            if (objId.StartsWith("Rooms/blacksmith_storage", StringComparison.OrdinalIgnoreCase))
                return objId;
        }
        return null;
    }

    public void Respawn(IMudContext ctx)
    {
        // Called when stock is replenished
    }
}
