namespace JitRealm.World.Std;

using JitRealm.Mud;

/// <summary>
/// Base class for all armor pieces.
/// Extend this class to create helmets, chestplates, boots, etc.
/// </summary>
public class ArmorBase : ItemBase, IArmor
{
    public virtual EquipmentSlot Slot => EquipmentSlot.Body;
    public virtual int ArmorClass => Ctx?.State.Get<int>("armor_class") ?? 1;
    public virtual string ArmorType => Ctx?.State.Get<string>("armor_type") ?? "cloth";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);

        // Initialize armor defaults if not set
        if (!ctx.State.Has("armor_class"))
        {
            ctx.State.Set("armor_class", 1);
        }
        if (!ctx.State.Has("armor_type"))
        {
            ctx.State.Set("armor_type", "cloth");
        }
    }

    public virtual void OnEquip(string whoId, IMudContext ctx)
    {
        ctx.Say($"puts on {ShortDescription}.");
    }

    public virtual void OnUnequip(string whoId, IMudContext ctx)
    {
        // Default: no special behavior
    }

    /// <summary>
    /// Set the armor class value.
    /// </summary>
    public void SetArmorClass(int ac, IMudContext ctx)
    {
        ctx.State.Set("armor_class", ac);
    }

    /// <summary>
    /// Set the armor type (e.g., "cloth", "leather", "chain", "plate").
    /// </summary>
    public void SetArmorType(string armorType, IMudContext ctx)
    {
        ctx.State.Set("armor_type", armorType);
    }
}

/// <summary>
/// Helmet armor piece.
/// </summary>
public class HelmetBase : ArmorBase
{
    public override EquipmentSlot Slot => EquipmentSlot.Head;
}

/// <summary>
/// Body armor piece (chestplate, robe, etc).
/// </summary>
public class ChestArmorBase : ArmorBase
{
    public override EquipmentSlot Slot => EquipmentSlot.Body;
}

/// <summary>
/// Gloves/gauntlets armor piece.
/// </summary>
public class GlovesBase : ArmorBase
{
    public override EquipmentSlot Slot => EquipmentSlot.Hands;
}

/// <summary>
/// Boots/shoes armor piece.
/// </summary>
public class BootsBase : ArmorBase
{
    public override EquipmentSlot Slot => EquipmentSlot.Feet;
}

/// <summary>
/// Leg armor piece (pants, greaves, etc).
/// </summary>
public class LeggingsBase : ArmorBase
{
    public override EquipmentSlot Slot => EquipmentSlot.Legs;
}

/// <summary>
/// Shield that goes in off-hand.
/// </summary>
public class ShieldBase : ArmorBase
{
    public override EquipmentSlot Slot => EquipmentSlot.OffHand;
}
