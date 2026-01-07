namespace JitRealm.Mud.Commands.Inventory;

/// <summary>
/// Command to eat food items.
/// </summary>
public class EatCommand : CommandBase
{
    public override string Name => "eat";
    public override IReadOnlyList<string> Aliases => Array.Empty<string>();
    public override string Usage => "eat <item>";
    public override string Description => "Eat a food item from your inventory";
    public override string Category => "Inventory";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.Output("Eat what?");
            return;
        }

        var itemName = string.Join(" ", args);
        await ConsumeHelper.ConsumeItemAsync(context, itemName, ConsumptionType.Food, "eat");
    }
}

/// <summary>
/// Command to drink beverage items.
/// </summary>
public class DrinkCommand : CommandBase
{
    public override string Name => "drink";
    public override IReadOnlyList<string> Aliases => new[] { "quaff" };
    public override string Usage => "drink <item>";
    public override string Description => "Drink a beverage from your inventory";
    public override string Category => "Inventory";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.Output("Drink what?");
            return;
        }

        var itemName = string.Join(" ", args);
        await ConsumeHelper.ConsumeItemAsync(context, itemName, ConsumptionType.Drink, "drink");
    }
}

/// <summary>
/// Helper class for consuming items.
/// </summary>
internal static class ConsumeHelper
{
    public static async Task ConsumeItemAsync(
        CommandContext context,
        string itemName,
        ConsumptionType requiredType,
        string verb)
    {
        var playerId = context.PlayerId;

        // Find the item in player's inventory
        var itemId = FindItemInInventory(context, itemName);
        if (itemId is null)
        {
            context.Output($"You're not carrying any '{itemName}'.");
            return;
        }

        var item = context.State.Objects?.Get<IItem>(itemId);
        if (item is null)
        {
            context.Output("That item doesn't exist.");
            return;
        }

        // Check if it's consumable
        if (item is not IConsumable consumable)
        {
            context.Output($"You can't {verb} {item.ShortDescription}.");
            return;
        }

        // Check consumption type compatibility
        if (!IsCompatibleType(consumable.ConsumptionType, requiredType))
        {
            var correctVerb = consumable.ConsumptionType == ConsumptionType.Food ? "eat" : "drink";
            context.Output($"You can't {verb} {item.ShortDescription}. Try '{correctVerb}' instead.");
            return;
        }

        // Create context for the item and call OnUse
        var itemCtx = context.CreateContext(itemId);
        consumable.OnUse(playerId, itemCtx);

        // Destruct the consumed item
        context.State.Containers.Remove(itemId);
        await context.State.Objects!.DestructAsync(itemId, context.State);
    }

    private static string? FindItemInInventory(CommandContext context, string name)
    {
        if (context.State.Objects is null) return null;

        var normalizedName = name.ToLowerInvariant();
        var inventory = context.State.Containers.GetContents(context.PlayerId);

        foreach (var itemId in inventory)
        {
            var obj = context.State.Objects.Get<IMudObject>(itemId);
            if (obj is null) continue;

            // Check main name
            if (obj.Name.ToLowerInvariant().Contains(normalizedName))
                return itemId;

            // Check aliases
            if (obj is IItem item)
            {
                foreach (var alias in item.Aliases)
                {
                    if (alias.ToLowerInvariant().Contains(normalizedName) ||
                        normalizedName.Contains(alias.ToLowerInvariant()))
                        return itemId;
                }

                // Check short description
                if (item.ShortDescription.ToLowerInvariant().Contains(normalizedName))
                    return itemId;
            }
        }

        return null;
    }

    private static bool IsCompatibleType(ConsumptionType itemType, ConsumptionType requiredType)
    {
        // Either type is always compatible
        if (itemType == ConsumptionType.Either)
            return true;

        // Otherwise must match exactly
        return itemType == requiredType;
    }
}
