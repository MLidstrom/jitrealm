namespace JitRealm.Mud;

// Optional hook interfaces (lpMUD-ish).

public interface IOnLoad
{
    void OnLoad(IMudContext ctx);
}

public interface IOnEnter
{
    void OnEnter(IMudContext ctx, string whoId);
}

public interface IOnLeave
{
    void OnLeave(IMudContext ctx, string whoId);
}

public interface IHeartbeat
{
    TimeSpan HeartbeatInterval { get; }
    void Heartbeat(IMudContext ctx);
}

public interface IResettable
{
    void Reset(IMudContext ctx);
}

public interface IOnReload
{
    /// <summary>
    /// Called after a blueprint reload, before the old instance is discarded.
    /// Allows custom state migration or reinitialization logic.
    /// </summary>
    /// <param name="ctx">Context with preserved state store</param>
    /// <param name="oldTypeName">Fully qualified name of the previous type</param>
    void OnReload(IMudContext ctx, string oldTypeName);
}

// Living object hooks (Phase 8)

/// <summary>
/// Hook called when a living object is about to take damage.
/// Allows modification of the damage amount before it is applied.
/// </summary>
public interface IOnDamage
{
    /// <summary>
    /// Called when about to take damage.
    /// </summary>
    /// <param name="amount">The incoming damage amount</param>
    /// <param name="attackerId">ID of the attacker, or null for environmental damage</param>
    /// <param name="ctx">The mud context</param>
    /// <returns>The modified damage amount to actually apply</returns>
    int OnDamage(int amount, string? attackerId, IMudContext ctx);
}

/// <summary>
/// Hook called when a living object dies (HP reaches 0).
/// </summary>
public interface IOnDeath
{
    /// <summary>
    /// Called when HP reaches 0.
    /// </summary>
    /// <param name="killerId">ID of the killer, or null for environmental death</param>
    /// <param name="ctx">The mud context</param>
    void OnDeath(string? killerId, IMudContext ctx);
}

/// <summary>
/// Hook called when a living object is healed.
/// </summary>
public interface IOnHeal
{
    /// <summary>
    /// Called when healed.
    /// </summary>
    /// <param name="amount">Amount healed</param>
    /// <param name="ctx">The mud context</param>
    void OnHeal(int amount, IMudContext ctx);
}

// Combat hooks (Phase 12)

/// <summary>
/// Hook called when a combatant is about to attack.
/// Allows modification of the outgoing damage.
/// </summary>
public interface IOnAttack
{
    /// <summary>
    /// Called when about to attack a target.
    /// </summary>
    /// <param name="targetId">ID of the target being attacked</param>
    /// <param name="baseDamage">Base damage before modifications</param>
    /// <param name="ctx">The mud context</param>
    /// <returns>The modified damage to deal</returns>
    int OnAttack(string targetId, int baseDamage, IMudContext ctx);
}

/// <summary>
/// Hook called when a combatant is about to be hit in combat.
/// Allows modification of the incoming damage (armor mitigation, etc).
/// </summary>
public interface IOnDefend
{
    /// <summary>
    /// Called when about to receive damage in combat.
    /// </summary>
    /// <param name="attackerId">ID of the attacker</param>
    /// <param name="incomingDamage">Incoming damage amount</param>
    /// <param name="ctx">The mud context</param>
    /// <returns>The modified damage to actually take</returns>
    int OnDefend(string attackerId, int incomingDamage, IMudContext ctx);
}

/// <summary>
/// Hook called when a combatant kills something.
/// </summary>
public interface IOnKill
{
    /// <summary>
    /// Called when this object kills something.
    /// </summary>
    /// <param name="victimId">ID of the killed object</param>
    /// <param name="ctx">The mud context</param>
    void OnKill(string victimId, IMudContext ctx);
}
