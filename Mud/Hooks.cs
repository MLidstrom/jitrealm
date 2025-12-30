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
