using System;
using JitRealm.Mud;

/// <summary>
/// Base class for all living beings (players, NPCs, monsters).
/// Manages HP via IStateStore for persistence across reloads.
/// Provides natural regeneration via heartbeat.
/// </summary>
public abstract class LivingBase : MudObjectBase, ILiving, IOnLoad, IHeartbeat
{
    /// <summary>
    /// Cached context for property access.
    /// Set during OnLoad.
    /// </summary>
    protected IMudContext? Ctx { get; private set; }

    /// <summary>
    /// Current hit points. Stored in IStateStore for persistence.
    /// </summary>
    public int HP => Ctx?.State.Get<int>("hp") ?? 0;

    /// <summary>
    /// Maximum hit points. Override in derived classes.
    /// </summary>
    public virtual int MaxHP => 100;

    /// <summary>
    /// Whether this living is alive (HP > 0).
    /// </summary>
    public bool IsAlive => HP > 0;

    /// <summary>
    /// Heartbeat interval for regeneration.
    /// Override to customize regeneration rate.
    /// </summary>
    public virtual TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(2);

    /// <summary>
    /// Amount healed per heartbeat tick. Override to customize.
    /// </summary>
    protected virtual int RegenAmount => 1;

    /// <summary>
    /// Called when the living is loaded or created.
    /// Initializes HP if not already set.
    /// </summary>
    public virtual void OnLoad(IMudContext ctx)
    {
        Ctx = ctx;

        // Initialize HP if not set (new instance)
        if (!HasStateKey(ctx, "hp"))
        {
            ctx.State.Set("hp", MaxHP);
        }
    }

    /// <summary>
    /// Called periodically for regeneration and other timed effects.
    /// </summary>
    public virtual void Heartbeat(IMudContext ctx)
    {
        Ctx = ctx;

        // Natural regeneration: heal if alive and not at max HP
        if (IsAlive && HP < MaxHP)
        {
            var healAmount = Math.Min(RegenAmount, MaxHP - HP);
            HealInternal(healAmount, ctx);
        }
    }

    /// <summary>
    /// Take damage from an attacker or environmental source.
    /// </summary>
    public virtual void TakeDamage(int amount, string? attackerId, IMudContext ctx)
    {
        Ctx = ctx;

        if (!IsAlive || amount <= 0)
            return;

        // Allow IOnDamage to modify the damage
        if (this is IOnDamage onDamage)
        {
            amount = onDamage.OnDamage(amount, attackerId, ctx);
        }

        // Apply damage
        var newHp = Math.Max(0, HP - amount);
        ctx.State.Set("hp", newHp);

        // Check for death
        if (newHp <= 0)
        {
            Die(attackerId, ctx);
        }
    }

    /// <summary>
    /// Heal this living by the specified amount.
    /// </summary>
    public virtual void Heal(int amount, IMudContext ctx)
    {
        Ctx = ctx;

        if (!IsAlive || amount <= 0)
            return;

        HealInternal(amount, ctx);
    }

    /// <summary>
    /// Internal healing logic, also called during regeneration.
    /// </summary>
    protected virtual void HealInternal(int amount, IMudContext ctx)
    {
        var newHp = Math.Min(MaxHP, HP + amount);
        ctx.State.Set("hp", newHp);

        // Notify via hook
        if (this is IOnHeal onHeal)
        {
            onHeal.OnHeal(amount, ctx);
        }
    }

    /// <summary>
    /// Called when HP reaches 0. Override to customize death behavior.
    /// </summary>
    public virtual void Die(string? killerId, IMudContext ctx)
    {
        Ctx = ctx;

        // Announce death
        ctx.Emote("collapses to the ground!");

        // Notify via hook
        if (this is IOnDeath onDeath)
        {
            onDeath.OnDeath(killerId, ctx);
        }
    }

    /// <summary>
    /// Fully restore HP to maximum.
    /// </summary>
    public virtual void FullHeal(IMudContext ctx)
    {
        Ctx = ctx;
        ctx.State.Set("hp", MaxHP);
    }

    /// <summary>
    /// Check if a state key exists.
    /// </summary>
    private static bool HasStateKey(IMudContext ctx, string key)
    {
        foreach (var k in ctx.State.Keys)
        {
            if (k == key) return true;
        }
        return false;
    }
}
