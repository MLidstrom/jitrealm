namespace JitRealm.Mud.Commands.Equipment;

/// <summary>
/// Unequip an item from an equipment slot or by item name.
/// </summary>
public class UnequipCommand : CommandBase
{
    public override string Name => "unequip";
    public override IReadOnlyList<string> Aliases => new[] { "remove" };
    public override string Usage => "unequip <item or slot>";
    public override string Description => "Remove equipped item";
    public override string Category => "Equipment";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var input = JoinArgs(args).ToLowerInvariant();

        // First, try to find equipped item by name/alias
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var equippedId = context.State.Equipment.GetEquipped(context.PlayerId, slot);
            if (equippedId is null) continue;

            var equippedItem = context.State.Objects!.Get<IEquippable>(equippedId);
            if (equippedItem is null) continue;

            // Check name, short description, and aliases
            var matches = equippedItem.Name.ToLowerInvariant().Contains(input) ||
                          equippedItem.ShortDescription.ToLowerInvariant().Contains(input);

            if (!matches && equippedItem is IItem item)
            {
                matches = item.Aliases.Any(a => a.ToLowerInvariant().Contains(input) ||
                                                 input.Contains(a.ToLowerInvariant()));
            }

            if (matches)
            {
                // Call OnUnequip hook
                var itemCtx = context.CreateContext(equippedId);
                equippedItem.OnUnequip(context.PlayerId, itemCtx);

                // Unequip the item
                context.State.Equipment.Unequip(context.PlayerId, slot);

                context.Output($"You unequip {equippedItem.ShortDescription}.");
                return Task.CompletedTask;
            }
        }

        // Fall back to slot name parsing
        if (Enum.TryParse<EquipmentSlot>(input, ignoreCase: true, out var parsedSlot))
        {
            return UnequipBySlot(context, parsedSlot);
        }

        // Try partial slot match
        var matchingSlots = Enum.GetValues<EquipmentSlot>()
            .Where(s => s.ToString().StartsWith(input, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingSlots.Count == 1)
        {
            return UnequipBySlot(context, matchingSlots[0]);
        }

        if (matchingSlots.Count > 1)
        {
            context.Output($"Ambiguous: '{input}'. Did you mean: {string.Join(", ", matchingSlots)}?");
            return Task.CompletedTask;
        }

        context.Output($"You don't have '{JoinArgs(args)}' equipped.");
        return Task.CompletedTask;
    }

    private static Task UnequipBySlot(CommandContext context, EquipmentSlot slot)
    {
        var itemId = context.State.Equipment.GetEquipped(context.PlayerId, slot);
        if (itemId is null)
        {
            context.Output($"Nothing is equipped in {slot}.");
            return Task.CompletedTask;
        }

        var item = context.State.Objects!.Get<IEquippable>(itemId);
        if (item is not null)
        {
            var itemCtx = context.CreateContext(itemId);
            item.OnUnequip(context.PlayerId, itemCtx);
        }

        context.State.Equipment.Unequip(context.PlayerId, slot);
        context.Output($"You unequip {item?.ShortDescription ?? itemId}.");
        return Task.CompletedTask;
    }
}
