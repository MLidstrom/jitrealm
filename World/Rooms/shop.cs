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
public sealed class Shop : MudObjectBase, IRoom, IResettable, ISpawner, IHasCommands, IHasLinkedRooms
{
    public override string Name => "The General Store";

    public string Description =>
        "A cluttered but cozy shop filled with all manner of goods. " +
        "Dusty shelves line the walls, stacked with potions, weapons, and curious trinkets. " +
        "A worn wooden counter separates the merchandise from a small backroom. " +
        "A wooden sign on the wall lists items for sale.";

    public IReadOnlyDictionary<string, string> Exits => new Dictionary<string, string>
    {
        ["west"] = "Rooms/start.cs"
    };

    public IReadOnlyList<string> Contents => Array.Empty<string>();

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

        // Calculate price (Value * 1.5, rounded to nearest 5)
        var price = CalculatePrice(item.Value);

        // Check player's gold
        var playerState = ctx.World.GetStateStore(playerId);
        var playerGold = playerState?.Get<int>("gold") ?? 0;

        if (playerGold < price)
        {
            ctx.Tell(playerId, $"That costs {price} gold. You only have {playerGold} gold.");
            return;
        }

        // Check if player can carry it
        var player = ctx.World.GetObject<IPlayer>(playerId);
        if (player != null && !player.CanCarry(item.Weight))
        {
            ctx.Tell(playerId, "You can't carry any more weight.");
            return;
        }

        // Complete the transaction
        playerState?.Set("gold", playerGold - price);
        ctx.Move(itemId, playerId);

        ctx.Tell(playerId, $"You purchase {item.ShortDescription} for {price} gold.");
        ctx.Say($"Thanks for your purchase!");
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

        // Calculate sell price (half of base value)
        var sellPrice = Math.Max(1, item.Value / 2);

        // Find storage room to move item to (or just destruct it)
        var storageId = FindStorageRoom(ctx);

        // Add gold to player
        var playerState = ctx.World.GetStateStore(playerId);
        var playerGold = playerState?.Get<int>("gold") ?? 0;
        playerState?.Set("gold", playerGold + sellPrice);

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

        ctx.Tell(playerId, $"You sell {item.ShortDescription} for {sellPrice} gold.");
        ctx.Say($"Pleasure doing business with you!");
    }

    private static int CalculatePrice(int value)
    {
        return ((int)(value * 1.5) + 2) / 5 * 5;
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

    public void Reset(IMudContext ctx)
    {
        ctx.Say("The shopkeeper tidies up the merchandise.");
    }
}
