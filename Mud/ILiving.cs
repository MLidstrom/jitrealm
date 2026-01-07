namespace JitRealm.Mud;

/// <summary>
/// Interface for living objects that have health and can take damage.
/// This is the foundation for players, NPCs, and monsters.
/// </summary>
public interface ILiving : IMudObject
{
    /// <summary>
    /// Detailed description shown when examining this living being.
    /// </summary>
    new string Description { get; }

    /// <summary>
    /// Alternative names players can use to reference this living being.
    /// For NPCs, this typically includes their character name (e.g., "barnaby")
    /// and role (e.g., "merchant", "keeper") in addition to their type name.
    /// </summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// Brief description shown in room listings.
    /// Example: "a shopkeeper", "the goblin"
    /// Default implementation adds an article to Name.
    /// </summary>
    string ShortDescription { get; }

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
