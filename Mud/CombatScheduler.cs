namespace JitRealm.Mud;

/// <summary>
/// Represents an active combat session between two combatants.
/// </summary>
public sealed class CombatSession
{
    public required string AttackerId { get; init; }
    public required string DefenderId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset LastRoundAt { get; set; }
}

/// <summary>
/// Schedules and processes combat rounds between combatants.
/// Combat happens automatically each tick for all active combats.
/// </summary>
public sealed class CombatScheduler
{
    /// <summary>
    /// Interval between combat rounds.
    /// </summary>
    public static readonly TimeSpan CombatRoundInterval = TimeSpan.FromSeconds(3);

    // Active combats: attackerId -> combat session
    private readonly Dictionary<string, CombatSession> _combats = new();

    // Random for damage calculation
    private readonly Random _random = new();

    /// <summary>
    /// Start combat between an attacker and defender.
    /// </summary>
    /// <param name="attackerId">The attacker's object ID</param>
    /// <param name="defenderId">The defender's object ID</param>
    /// <param name="now">Current time</param>
    public void StartCombat(string attackerId, string defenderId, DateTimeOffset now)
    {
        _combats[attackerId] = new CombatSession
        {
            AttackerId = attackerId,
            DefenderId = defenderId,
            StartedAt = now,
            LastRoundAt = now
        };
    }

    /// <summary>
    /// End combat for a specific combatant.
    /// </summary>
    /// <param name="combatantId">The combatant to remove from combat</param>
    public void EndCombat(string combatantId)
    {
        _combats.Remove(combatantId);

        // Also remove any combat targeting this combatant
        var toRemove = _combats
            .Where(kvp => kvp.Value.DefenderId == combatantId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _combats.Remove(key);
        }
    }

    /// <summary>
    /// Check if a combatant is in combat.
    /// </summary>
    public bool IsInCombat(string combatantId)
    {
        return _combats.ContainsKey(combatantId) ||
               _combats.Values.Any(c => c.DefenderId == combatantId);
    }

    /// <summary>
    /// Get the target of a combatant's attack.
    /// </summary>
    public string? GetCombatTarget(string attackerId)
    {
        return _combats.TryGetValue(attackerId, out var session) ? session.DefenderId : null;
    }

    /// <summary>
    /// Get all active combat sessions.
    /// </summary>
    public IEnumerable<CombatSession> GetActiveCombats()
    {
        return _combats.Values.ToList();
    }

    /// <summary>
    /// Process all due combat rounds.
    /// </summary>
    /// <param name="state">The world state</param>
    /// <param name="clock">The game clock</param>
    /// <param name="sendMessage">Function to send messages to players</param>
    /// <returns>List of (killerId, victimId) for deaths that occurred</returns>
    public List<(string killerId, string victimId)> ProcessCombatRounds(
        WorldState state,
        IClock clock,
        Action<string, string> sendMessage)
    {
        var deaths = new List<(string killerId, string victimId)>();
        var now = clock.Now;

        var dueCombats = _combats.Values
            .Where(c => now - c.LastRoundAt >= CombatRoundInterval)
            .ToList();

        foreach (var combat in dueCombats)
        {
            // Check if both combatants still exist and are alive
            var attacker = state.Objects?.Get<ILiving>(combat.AttackerId);
            var defender = state.Objects?.Get<ILiving>(combat.DefenderId);

            if (attacker is null || defender is null || !attacker.IsAlive || !defender.IsAlive)
            {
                EndCombat(combat.AttackerId);
                continue;
            }

            // Check if they're still in the same room
            var attackerRoom = state.Containers.GetContainer(combat.AttackerId);
            var defenderRoom = state.Containers.GetContainer(combat.DefenderId);

            if (attackerRoom != defenderRoom)
            {
                sendMessage(combat.AttackerId, "Your target has left the room. Combat ends.");
                EndCombat(combat.AttackerId);
                continue;
            }

            // Process combat round
            var damage = CalculateDamage(attacker, defender, state, clock, sendMessage);

            // Apply damage
            var ctx = state.CreateContext(combat.DefenderId, clock);
            defender.TakeDamage(damage, combat.AttackerId, ctx);

            // Update last round time
            combat.LastRoundAt = now;

            // Check for death
            if (!defender.IsAlive)
            {
                deaths.Add((combat.AttackerId, combat.DefenderId));
                EndCombat(combat.AttackerId);
            }
        }

        return deaths;
    }

    /// <summary>
    /// Calculate damage for a combat round.
    /// </summary>
    private int CalculateDamage(
        ILiving attacker,
        ILiving defender,
        WorldState state,
        IClock clock,
        Action<string, string> sendMessage)
    {
        // Get weapon damage range
        int minDamage = 1;
        int maxDamage = 2;

        if (attacker is IHasEquipment hasEquip)
        {
            var (min, max) = hasEquip.WeaponDamage;
            minDamage = min;
            maxDamage = max;
        }

        // Roll base damage
        int baseDamage = _random.Next(minDamage, maxDamage + 1);

        // Apply OnAttack hook if available
        var attackerCtx = state.CreateContext(attacker.Id, clock);
        if (attacker is IOnAttack onAttack)
        {
            baseDamage = onAttack.OnAttack(defender.Id, baseDamage, attackerCtx);
        }

        // Get armor reduction
        int armorReduction = 0;
        if (defender is IHasEquipment defenderEquip)
        {
            armorReduction = defenderEquip.TotalArmorClass;
        }

        // Apply armor reduction (minimum 1 damage)
        int damage = Math.Max(1, baseDamage - armorReduction);

        // Apply OnDefend hook if available
        var defenderCtx = state.CreateContext(defender.Id, clock);
        if (defender is IOnDefend onDefend)
        {
            damage = onDefend.OnDefend(attacker.Id, damage, defenderCtx);
        }

        // Ensure minimum 1 damage
        damage = Math.Max(1, damage);

        // Send combat messages
        var attackerName = attacker.Name;
        var defenderName = defender.Name;

        sendMessage(attacker.Id, $"You attack {defenderName} for {damage} damage!");
        sendMessage(defender.Id, $"{attackerName} attacks you for {damage} damage!");

        // Send to room (except attacker and defender)
        var roomId = state.Containers.GetContainer(attacker.Id);
        if (roomId is not null)
        {
            foreach (var otherId in state.Containers.GetContents(roomId))
            {
                if (otherId != attacker.Id && otherId != defender.Id)
                {
                    var other = state.Objects?.Get<IMudObject>(otherId);
                    if (other is IPlayer)
                    {
                        sendMessage(otherId, $"{attackerName} attacks {defenderName}!");
                    }
                }
            }
        }

        return damage;
    }

    /// <summary>
    /// Attempt to flee from combat.
    /// </summary>
    /// <param name="combatantId">The combatant trying to flee</param>
    /// <param name="state">World state for finding exits</param>
    /// <param name="clock">Game clock</param>
    /// <returns>The exit direction if flee was successful, null otherwise</returns>
    public string? AttemptFlee(string combatantId, WorldState state, IClock clock)
    {
        if (!IsInCombat(combatantId))
            return null;

        // 50% chance to flee
        if (_random.Next(100) < 50)
            return null;

        // Get the room and find a random exit
        var roomId = state.Containers.GetContainer(combatantId);
        if (roomId is null)
            return null;

        var room = state.Objects?.Get<IRoom>(roomId);
        if (room is null || room.Exits.Count == 0)
            return null;

        // Pick a random exit
        var exits = room.Exits.Keys.ToList();
        var exitDir = exits[_random.Next(exits.Count)];

        // End combat
        EndCombat(combatantId);

        return exitDir;
    }

    /// <summary>
    /// Get the number of active combats.
    /// </summary>
    public int ActiveCombatCount => _combats.Count;

    /// <summary>
    /// Clear all combats (for shutdown/reset).
    /// </summary>
    public void Clear()
    {
        _combats.Clear();
    }
}
