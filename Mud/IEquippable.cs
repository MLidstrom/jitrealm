namespace JitRealm.Mud;

/// <summary>
/// Equipment slots where items can be worn/wielded.
/// </summary>
public enum EquipmentSlot
{
    Head,
    Neck,
    Body,
    Back,
    Arms,
    Hands,
    Waist,
    Legs,
    Feet,
    MainHand,
    OffHand,
    Ring1,
    Ring2
}

/// <summary>
/// An item that can be equipped in a specific slot.
/// </summary>
public interface IEquippable : ICarryable
{
    /// <summary>Which slot this item equips to.</summary>
    EquipmentSlot Slot { get; }

    /// <summary>Called when the item is equipped.</summary>
    /// <param name="whoId">ID of the being equipping this item</param>
    /// <param name="ctx">MUD context</param>
    void OnEquip(string whoId, IMudContext ctx);

    /// <summary>Called when the item is unequipped.</summary>
    /// <param name="whoId">ID of the being unequipping this item</param>
    /// <param name="ctx">MUD context</param>
    void OnUnequip(string whoId, IMudContext ctx);
}

/// <summary>
/// A weapon that can be wielded.
/// </summary>
public interface IWeapon : IEquippable
{
    /// <summary>Minimum damage this weapon deals.</summary>
    int MinDamage { get; }

    /// <summary>Maximum damage this weapon deals.</summary>
    int MaxDamage { get; }

    /// <summary>Weapon type (sword, axe, dagger, etc.).</summary>
    string WeaponType { get; }
}

/// <summary>
/// Armor that can be worn for protection.
/// </summary>
public interface IArmor : IEquippable
{
    /// <summary>Armor class bonus provided by this armor.</summary>
    int ArmorClass { get; }

    /// <summary>Armor type (cloth, leather, chain, plate).</summary>
    string ArmorType { get; }
}

/// <summary>
/// A living being that can equip items.
/// </summary>
public interface IHasEquipment : ILiving
{
    /// <summary>Total armor class from all equipped armor pieces.</summary>
    int TotalArmorClass { get; }

    /// <summary>Weapon damage range from equipped weapon(s).</summary>
    (int min, int max) WeaponDamage { get; }
}
