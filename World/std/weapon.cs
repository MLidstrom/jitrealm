namespace JitRealm.World.Std;

using JitRealm.Mud;

/// <summary>
/// Base class for all weapons.
/// Extend this class to create swords, axes, daggers, etc.
/// </summary>
public class WeaponBase : ItemBase, IWeapon
{
    /// <summary>
    /// Weapons don't stack - each is unique (can have different stats/enchantments).
    /// </summary>
    public override string StackKey => Id; // Unique per instance

    public virtual EquipmentSlot Slot => EquipmentSlot.MainHand;
    public virtual int MinDamage => Ctx?.State.Get<int>("min_damage") ?? 1;
    public virtual int MaxDamage => Ctx?.State.Get<int>("max_damage") ?? 4;
    public virtual string WeaponType => Ctx?.State.Get<string>("weapon_type") ?? "melee";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);

        // Initialize weapon defaults if not set
        if (!ctx.State.Has("min_damage"))
        {
            ctx.State.Set("min_damage", 1);
        }
        if (!ctx.State.Has("max_damage"))
        {
            ctx.State.Set("max_damage", 4);
        }
        if (!ctx.State.Has("weapon_type"))
        {
            ctx.State.Set("weapon_type", "melee");
        }
    }

    public virtual void OnEquip(string whoId, IMudContext ctx)
    {
        ctx.Say($"wields {ShortDescription}.");
    }

    public virtual void OnUnequip(string whoId, IMudContext ctx)
    {
        // Default: no special behavior
    }

    /// <summary>
    /// Set the weapon's minimum damage.
    /// </summary>
    public void SetMinDamage(int damage, IMudContext ctx)
    {
        ctx.State.Set("min_damage", damage);
    }

    /// <summary>
    /// Set the weapon's maximum damage.
    /// </summary>
    public void SetMaxDamage(int damage, IMudContext ctx)
    {
        ctx.State.Set("max_damage", damage);
    }

    /// <summary>
    /// Set the weapon type (e.g., "sword", "axe", "dagger").
    /// </summary>
    public void SetWeaponType(string weaponType, IMudContext ctx)
    {
        ctx.State.Set("weapon_type", weaponType);
    }
}

/// <summary>
/// A two-handed weapon that occupies both main hand and off-hand.
/// </summary>
public class TwoHandedWeaponBase : WeaponBase
{
    // TODO: Implement two-handed logic in equipment system
    // For now, just equips to main hand
}
