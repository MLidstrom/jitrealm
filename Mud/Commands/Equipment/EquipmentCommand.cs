namespace JitRealm.Mud.Commands.Equipment;

/// <summary>
/// Show currently equipped items.
/// </summary>
public class EquipmentCommand : CommandBase
{
    public override string Name => "equipment";
    public override IReadOnlyList<string> Aliases => new[] { "eq" };
    public override string Usage => "equipment";
    public override string Description => "Show equipped items";
    public override string Category => "Equipment";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var equipped = context.State.Equipment.GetAllEquipped(context.PlayerId);
        if (equipped.Count == 0)
        {
            context.Output("You have nothing equipped.");
            return Task.CompletedTask;
        }

        context.Output("You have equipped:");
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            if (equipped.TryGetValue(slot, out var itemId))
            {
                var item = context.State.Objects!.Get<IItem>(itemId);
                var desc = item?.ShortDescription ?? itemId;

                // Add extra info for weapons/armor
                if (item is IWeapon weapon)
                {
                    desc += $" ({weapon.MinDamage}-{weapon.MaxDamage} dmg)";
                }
                else if (item is IArmor armor)
                {
                    desc += $" ({armor.ArmorClass} AC)";
                }

                context.Output($"  {slot,-12}: {desc}");
            }
        }

        // Show totals
        int totalAC = 0;
        int minDmg = 0, maxDmg = 0;
        foreach (var kvp in equipped)
        {
            var item = context.State.Objects!.Get<IItem>(kvp.Value);
            if (item is IArmor armor)
            {
                totalAC += armor.ArmorClass;
            }
            if (item is IWeapon weapon)
            {
                minDmg += weapon.MinDamage;
                maxDmg += weapon.MaxDamage;
            }
        }

        if (totalAC > 0 || maxDmg > 0)
        {
            context.Output("");
            if (totalAC > 0) context.Output($"Total Armor Class: {totalAC}");
            if (maxDmg > 0) context.Output($"Weapon Damage: {minDmg}-{maxDmg}");
        }

        return Task.CompletedTask;
    }
}
