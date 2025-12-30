namespace JitRealm.Mud;

/// <summary>
/// Interface for living objects that have health and can take damage.
/// This is the foundation for players, NPCs, and monsters.
/// </summary>
public interface ILiving : IMudObject
{
    /// <summary>
    /// Current hit points.
    /// </summary>
    int HP { get; }

    /// <summary>
    /// Maximum hit points.
    /// </summary>
    int MaxHP { get; }

    /// <summary>
    /// Whether this living is currently alive (HP > 0).
    /// </summary>
    bool IsAlive => HP > 0;

    /// <summary>
    /// Called when this living takes damage.
    /// </summary>
    /// <param name="amount">Amount of damage to take</param>
    /// <param name="attackerId">ID of the attacker, or null for environmental damage</param>
    /// <param name="ctx">The mud context</param>
    void TakeDamage(int amount, string? attackerId, IMudContext ctx);

    /// <summary>
    /// Called when this living is healed.
    /// </summary>
    /// <param name="amount">Amount to heal</param>
    /// <param name="ctx">The mud context</param>
    void Heal(int amount, IMudContext ctx);

    /// <summary>
    /// Called when HP reaches 0.
    /// </summary>
    /// <param name="killerId">ID of the killer, or null for environmental death</param>
    /// <param name="ctx">The mud context</param>
    void Die(string? killerId, IMudContext ctx);
}

/// <summary>
/// Optional interface for living objects with detailed stats.
/// </summary>
public interface IHasStats : ILiving
{
    int Strength { get; }
    int Dexterity { get; }
    int Constitution { get; }
    int Intelligence { get; }
    int Wisdom { get; }
    int Charisma { get; }
}
