using System;
using JitRealm.Mud;

/// <summary>
/// Base class for monsters and aggressive NPCs.
/// Provides automatic aggression, respawning, and basic AI behaviors.
/// </summary>
public abstract class MonsterBase : LivingBase, IOnEnter, IOnDeath, IHasEquipment
{
    /// <summary>
    /// Experience points awarded when this monster is killed.
    /// Override to set the monster's XP value.
    /// </summary>
    public virtual int ExperienceValue => MaxHP;

    /// <summary>
    /// Whether this monster attacks players on sight.
    /// Override to make the monster passive.
    /// </summary>
    public virtual bool IsAggressive => true;

    /// <summary>
    /// Delay before attacking a player who enters (in seconds).
    /// Gives players a moment to react.
    /// </summary>
    public virtual int AggroDelaySeconds => 2;

    /// <summary>
    /// Time until respawn after death (in seconds).
    /// Override to customize respawn time.
    /// </summary>
    public virtual int RespawnDelaySeconds => 60;

    // WanderChance is now inherited from LivingBase (int 0-100)
    // Override to enable wandering for specific monsters

    /// <summary>
    /// Natural armor class from tough skin, scales, etc.
    /// Override to give the monster armor.
    /// </summary>
    public virtual int TotalArmorClass => 0;

    /// <summary>
    /// Natural weapon damage from claws, fangs, etc.
    /// Override to set the monster's attack power.
    /// </summary>
    public virtual (int min, int max) WeaponDamage => (1, 3);

    /// <summary>
    /// Called when something enters the same room as this monster.
    /// Aggressive monsters will attack players after a short delay.
    /// </summary>
    public virtual void OnEnter(IMudContext ctx, string whoId)
    {
        if (!IsAlive || !IsAggressive)
            return;

        // Check if the entering entity is a player
        var entering = ctx.World.GetObject<IPlayer>(whoId);
        if (entering is null)
            return;

        // React to the player
        ctx.Emote($"snarls at {entering.Name}!");

        // Schedule an attack after a delay
        ctx.CallOut(nameof(StartAttack), TimeSpan.FromSeconds(AggroDelaySeconds), whoId);
    }

    /// <summary>
    /// Called by callout to initiate combat with a target.
    /// </summary>
    public void StartAttack(IMudContext ctx, string targetId)
    {
        if (!IsAlive)
            return;

        // Check if target is still in the same room
        var myRoom = ctx.World.GetObjectLocation(Id);
        var targetRoom = ctx.World.GetObjectLocation(targetId);

        if (myRoom != targetRoom)
            return;  // Target left

        // Check if target is still alive
        var target = ctx.World.GetObject<ILiving>(targetId);
        if (target is null || !target.IsAlive)
            return;

        // Start combat via the combat scheduler
        // Note: Combat is initiated by the driver when this monster is attacked,
        // or we can emote that we're attacking
        ctx.Emote($"attacks {target.Name}!");

        // The actual combat is handled by CombatScheduler when the player attacks us
        // or when we're registered in combat
    }

    /// <summary>
    /// Called when the monster dies.
    /// Schedules respawn after the configured delay.
    /// </summary>
    public virtual void OnDeath(string? killerId, IMudContext ctx)
    {
        ctx.Emote("lets out a dying shriek!");

        // Schedule respawn
        ctx.CallOut(nameof(Respawn), TimeSpan.FromSeconds(RespawnDelaySeconds));
    }

    /// <summary>
    /// Called to respawn the monster after death.
    /// Restores HP and announces return.
    /// </summary>
    public virtual void Respawn(IMudContext ctx)
    {
        FullHeal(ctx);
        ctx.Emote("emerges from the shadows!");
    }

    // Wandering is now handled by LivingBase.TryWander() which actually moves the NPC
    // Just override WanderChance in subclasses to enable (e.g., override int WanderChance => 10; for 10% chance)
}
