using System.Collections.Generic;

namespace JitRealm.Mud;

/// <summary>
/// Interface for objects (typically rooms) that spawn NPCs/monsters.
/// Rooms implementing this interface will automatically spawn their NPCs
/// when loaded and respawn them periodically.
/// </summary>
public interface ISpawner
{
    /// <summary>
    /// Blueprints to spawn and their maximum counts.
    /// Key: blueprint path (e.g., "npcs/goblin.cs")
    /// Value: maximum number of instances to maintain
    /// </summary>
    IReadOnlyDictionary<string, int> Spawns { get; }

    /// <summary>
    /// Called periodically to replenish spawns that have died or been removed.
    /// The driver calls this during reset or on a timer.
    /// </summary>
    /// <param name="ctx">The mud context for this spawner.</param>
    void Respawn(IMudContext ctx);
}
