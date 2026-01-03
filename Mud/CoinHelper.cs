using System.Text;
using System.Text.RegularExpressions;

namespace JitRealm.Mud;

/// <summary>
/// Utility methods for coin manipulation across the driver.
/// Handles parsing, formatting, finding, adding, and deducting coins.
/// </summary>
public static class CoinHelper
{
    /// <summary>
    /// Parse commands like "50 gold", "100 gc", "25 silver coins".
    /// Returns (amount, material) or null if not a coin command.
    /// </summary>
    public static (int Amount, CoinMaterial Material)? ParseCoinCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var parts = input.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        if (!int.TryParse(parts[0], out var amount) || amount <= 0)
            return null;

        // Handle "50 gold", "50 gold coins", "50 gc"
        var materialStr = parts[1];
        if (materialStr.EndsWith("coins"))
            materialStr = materialStr[..^5].Trim();
        else if (materialStr.EndsWith("coin"))
            materialStr = materialStr[..^4].Trim();

        return materialStr switch
        {
            "gold" or "gc" or "g" => (amount, CoinMaterial.Gold),
            "silver" or "sc" or "s" => (amount, CoinMaterial.Silver),
            "copper" or "cc" or "c" => (amount, CoinMaterial.Copper),
            _ => null
        };
    }

    /// <summary>
    /// Parse a material name without amount (e.g., "gold", "gc", "silver coins").
    /// Returns the material or null if not recognized.
    /// </summary>
    public static CoinMaterial? ParseMaterial(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var materialStr = input.Trim().ToLowerInvariant();
        if (materialStr.EndsWith("coins"))
            materialStr = materialStr[..^5].Trim();
        else if (materialStr.EndsWith("coin"))
            materialStr = materialStr[..^4].Trim();

        return materialStr switch
        {
            "gold" or "gc" or "g" => CoinMaterial.Gold,
            "silver" or "sc" or "s" => CoinMaterial.Silver,
            "copper" or "cc" or "c" => CoinMaterial.Copper,
            _ => null
        };
    }

    /// <summary>
    /// Find a coin pile of specific material in a container.
    /// </summary>
    public static string? FindCoinPile(WorldState state, string containerId, CoinMaterial material)
    {
        var contents = state.Containers.GetContents(containerId);
        foreach (var itemId in contents)
        {
            var coin = state.Objects?.Get<ICoin>(itemId);
            if (coin?.Material == material)
                return itemId;
        }
        return null;
    }

    /// <summary>
    /// Calculate total value in copper from all coin piles in a container.
    /// </summary>
    public static int GetTotalCopperValue(WorldState state, string containerId)
    {
        int total = 0;
        var contents = state.Containers.GetContents(containerId);
        foreach (var itemId in contents)
        {
            var coin = state.Objects?.Get<ICoin>(itemId);
            if (coin is not null)
            {
                total += coin.Amount * (int)coin.Material;
            }
        }
        return total;
    }

    /// <summary>
    /// Get the amount of a specific coin material in a container.
    /// </summary>
    public static int GetCoinAmount(WorldState state, string containerId, CoinMaterial material)
    {
        var coinId = FindCoinPile(state, containerId, material);
        if (coinId is null)
            return 0;

        var coin = state.Objects?.Get<ICoin>(coinId);
        return coin?.Amount ?? 0;
    }

    /// <summary>
    /// Format coin amount for display (e.g., "1 gold coin", "50 silver coins").
    /// </summary>
    public static string FormatCoins(int amount, CoinMaterial material)
    {
        var name = material.ToString().ToLower();
        return amount == 1 ? $"1 {name} coin" : $"{amount} {name} coins";
    }

    /// <summary>
    /// Format a copper value as a breakdown (e.g., "1 GC, 50 SC").
    /// Only shows non-zero denominations.
    /// </summary>
    public static string FormatPrice(int copperAmount)
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

    /// <summary>
    /// Format the total wealth in a container as a summary (e.g., "75 GC, 50 SC, 0 CC").
    /// Always shows all three denominations.
    /// </summary>
    public static string FormatWealth(WorldState state, string containerId)
    {
        var gold = GetCoinAmount(state, containerId, CoinMaterial.Gold);
        var silver = GetCoinAmount(state, containerId, CoinMaterial.Silver);
        var copper = GetCoinAmount(state, containerId, CoinMaterial.Copper);

        return $"{gold} GC, {silver} SC, {copper} CC";
    }

    /// <summary>
    /// Add coins to a container, merging with existing piles of same material.
    /// Returns the coin instance ID.
    /// </summary>
    public static async Task<string?> AddCoinsAsync(WorldState state, string containerId, int amount, CoinMaterial material)
    {
        if (amount <= 0 || state.Objects is null)
            return null;

        var existingId = FindCoinPile(state, containerId, material);
        if (existingId is not null)
        {
            // Merge with existing pile
            var coinState = state.Objects.GetStateStore(existingId);
            var existingAmount = coinState?.Get<int>("amount") ?? 0;
            coinState?.Set("amount", existingAmount + amount);
            return existingId;
        }
        else
        {
            // Create new coin instance
            var newCoin = await state.Objects.CloneAsync<ICoin>("Items/coin.cs", state);
            var newCoinState = state.Objects.GetStateStore(newCoin.Id);
            newCoinState?.Set("material", material.ToString());
            newCoinState?.Set("amount", amount);
            state.Containers.Add(containerId, newCoin.Id);
            return newCoin.Id;
        }
    }

    /// <summary>
    /// Deduct coins from a container, preferring smaller denominations first.
    /// Returns true if successful, false if insufficient funds.
    /// </summary>
    public static async Task<bool> DeductCoinsAsync(WorldState state, string containerId, int copperAmount)
    {
        if (copperAmount <= 0)
            return true;

        var available = GetTotalCopperValue(state, containerId);
        if (available < copperAmount)
            return false;

        // Get current amounts
        var goldId = FindCoinPile(state, containerId, CoinMaterial.Gold);
        var silverId = FindCoinPile(state, containerId, CoinMaterial.Silver);
        var copperId = FindCoinPile(state, containerId, CoinMaterial.Copper);

        var goldAmount = goldId != null ? state.Objects!.Get<ICoin>(goldId)?.Amount ?? 0 : 0;
        var silverAmount = silverId != null ? state.Objects!.Get<ICoin>(silverId)?.Amount ?? 0 : 0;
        var copperAmount2 = copperId != null ? state.Objects!.Get<ICoin>(copperId)?.Amount ?? 0 : 0;

        // Convert everything to copper for calculation
        var totalCopper = goldAmount * 10000 + silverAmount * 100 + copperAmount2;
        var remainingCopper = totalCopper - copperAmount;

        // Calculate new amounts (optimal breakdown)
        var newGold = remainingCopper / 10000;
        var remaining = remainingCopper % 10000;
        var newSilver = remaining / 100;
        var newCopper = remaining % 100;

        // Update or remove piles
        await UpdateOrRemovePileAsync(state, containerId, goldId, CoinMaterial.Gold, newGold);
        await UpdateOrRemovePileAsync(state, containerId, silverId, CoinMaterial.Silver, newSilver);
        await UpdateOrRemovePileAsync(state, containerId, copperId, CoinMaterial.Copper, newCopper);

        return true;
    }

    /// <summary>
    /// Transfer coins from one container to another.
    /// Returns true if successful.
    /// </summary>
    public static async Task<bool> TransferCoinsAsync(WorldState state, string fromId, string toId, int amount, CoinMaterial material)
    {
        var fromCoinId = FindCoinPile(state, fromId, material);
        if (fromCoinId is null)
            return false;

        var fromCoin = state.Objects?.Get<ICoin>(fromCoinId);
        if (fromCoin is null || fromCoin.Amount < amount)
            return false;

        var fromState = state.Objects!.GetStateStore(fromCoinId);
        var currentAmount = fromState?.Get<int>("amount") ?? 0;

        if (amount == currentAmount)
        {
            // Move entire pile (will auto-merge at destination via Move())
            state.Containers.Move(fromCoinId, toId);

            // Check if there's an existing pile to merge with
            var existingId = FindCoinPile(state, toId, material);
            if (existingId != null && existingId != fromCoinId)
            {
                // Merge
                var existingState = state.Objects.GetStateStore(existingId);
                var existingAmount = existingState?.Get<int>("amount") ?? 0;
                existingState?.Set("amount", existingAmount + amount);

                // Remove the moved pile
                state.Containers.Remove(fromCoinId);
                await state.Objects.DestructAsync(fromCoinId, state);
            }
        }
        else
        {
            // Split: reduce source pile, add to destination
            fromState?.Set("amount", currentAmount - amount);
            await AddCoinsAsync(state, toId, amount, material);
        }

        return true;
    }

    private static async Task UpdateOrRemovePileAsync(WorldState state, string containerId, string? pileId, CoinMaterial material, int newAmount)
    {
        if (newAmount <= 0)
        {
            // Remove pile if it exists
            if (pileId != null)
            {
                state.Containers.Remove(pileId);
                await state.Objects!.DestructAsync(pileId, state);
            }
        }
        else if (pileId != null)
        {
            // Update existing pile
            var pileState = state.Objects!.GetStateStore(pileId);
            pileState?.Set("amount", newAmount);
        }
        else
        {
            // Create new pile
            await AddCoinsAsync(state, containerId, newAmount, material);
        }
    }
}
