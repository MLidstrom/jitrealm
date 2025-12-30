namespace JitRealm.Mud;

/// <summary>
/// Interface for living objects that can engage in combat.
/// Extends ILiving with combat-specific functionality.
/// </summary>
public interface ICombatant : ILiving
{
    /// <summary>
    /// Whether this combatant is currently in combat.
    /// </summary>
    bool InCombat { get; }

    /// <summary>
    /// The object ID of the current combat target, or null if not in combat.
    /// </summary>
    string? CombatTarget { get; }

    /// <summary>
    /// Initiate an attack against a target.
    /// </summary>
    /// <param name="targetId">The ID of the target to attack</param>
    /// <param name="ctx">The mud context</param>
    void Attack(string targetId, IMudContext ctx);

    /// <summary>
    /// Stop combat with current target.
    /// </summary>
    /// <param name="ctx">The mud context</param>
    void StopCombat(IMudContext ctx);

    /// <summary>
    /// Attempt to flee from combat.
    /// </summary>
    /// <param name="ctx">The mud context</param>
    /// <returns>True if flee was successful, false otherwise</returns>
    bool TryFlee(IMudContext ctx);
}
