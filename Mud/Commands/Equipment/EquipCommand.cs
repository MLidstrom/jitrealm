namespace JitRealm.Mud.Commands.Equipment;

/// <summary>
/// Equip an item from inventory.
/// </summary>
public class EquipCommand : CommandBase
{
    public override string Name => "equip";
    public override IReadOnlyList<string> Aliases => new[] { "wield", "wear" };
    public override string Usage => "equip <item>";
    public override string Description => "Equip an item";
    public override string Category => "Equipment";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var itemName = JoinArgs(args);

        // Find item in inventory
        var ctx = context.CreateContext(context.PlayerId);
        var itemId = ctx.FindItem(itemName, context.PlayerId);
        if (itemId is null)
        {
            context.Output($"You're not carrying '{itemName}'.");
            return Task.CompletedTask;
        }

        var item = context.State.Objects!.Get<IEquippable>(itemId);
        if (item is null)
        {
            context.Output("That can't be equipped.");
            return Task.CompletedTask;
        }

        // Check if something is already equipped in that slot
        var existingItemId = context.State.Equipment.GetEquipped(context.PlayerId, item.Slot);
        if (existingItemId is not null)
        {
            var existingItem = context.State.Objects.Get<IEquippable>(existingItemId);
            if (existingItem is not null)
            {
                // Unequip existing item first
                var existingItemCtx = context.CreateContext(existingItemId);
                existingItem.OnUnequip(context.PlayerId, existingItemCtx);
                context.Output($"You remove {existingItem.ShortDescription}.");
            }
        }

        // Equip the new item
        context.State.Equipment.Equip(context.PlayerId, item.Slot, itemId);

        // Call OnEquip hook
        var itemCtx = context.CreateContext(itemId);
        item.OnEquip(context.PlayerId, itemCtx);

        context.Output($"You equip {item.ShortDescription} ({item.Slot}).");
        return Task.CompletedTask;
    }
}
