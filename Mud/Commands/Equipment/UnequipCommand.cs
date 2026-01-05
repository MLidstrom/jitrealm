namespace JitRealm.Mud.Commands.Equipment;

/// <summary>
/// Unequip an item from an equipment slot.
/// </summary>
public class UnequipCommand : CommandBase
{
    public override string Name => "unequip";
    public override IReadOnlyList<string> Aliases => new[] { "remove" };
    public override string Usage => "unequip <slot>";
    public override string Description => "Remove equipped item";
    public override string Category => "Equipment";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var slotName = JoinArgs(args);

        // Try to parse slot name
        if (!Enum.TryParse<EquipmentSlot>(slotName, ignoreCase: true, out var slot))
        {
            // Try partial match
            var matchingSlots = Enum.GetValues<EquipmentSlot>()
                .Where(s => s.ToString().StartsWith(slotName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingSlots.Count == 1)
            {
                slot = matchingSlots[0];
            }
            else if (matchingSlots.Count > 1)
            {
                context.Output($"Ambiguous slot '{slotName}'. Did you mean: {string.Join(", ", matchingSlots)}?");
                return Task.CompletedTask;
            }
            else
            {
                context.Output($"Unknown slot '{slotName}'. Valid slots: {string.Join(", ", Enum.GetNames<EquipmentSlot>())}");
                return Task.CompletedTask;
            }
        }

        // Check if something is equipped in that slot
        var itemId = context.State.Equipment.GetEquipped(context.PlayerId, slot);
        if (itemId is null)
        {
            context.Output($"Nothing is equipped in {slot}.");
            return Task.CompletedTask;
        }

        var item = context.State.Objects!.Get<IEquippable>(itemId);
        if (item is not null)
        {
            // Call OnUnequip hook
            var itemCtx = context.CreateContext(itemId);
            item.OnUnequip(context.PlayerId, itemCtx);
        }

        // Unequip the item
        context.State.Equipment.Unequip(context.PlayerId, slot);

        context.Output($"You unequip {item?.ShortDescription ?? itemId}.");
        return Task.CompletedTask;
    }
}
