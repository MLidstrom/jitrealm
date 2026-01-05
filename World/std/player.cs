using System;
using JitRealm.Mud;

/// <summary>
/// Base class for player objects. Each connected player gets a clone of this blueprint.
/// Players extend LivingBase and implement IPlayer for player-specific functionality.
///
/// State stored in IStateStore (persists across sessions):
/// - hp: Current hit points
/// - player_name: The player's chosen name
/// - last_login: Timestamp of last login
/// - total_playtime: Total accumulated play time
/// - experience: Current XP
/// - level: Current level
/// </summary>
public class PlayerBase : LivingBase, IPlayer, IOnLoad
{
    // Constants for leveling
    private const int BaseXpPerLevel = 100;
    private const double XpMultiplier = 1.5;

    public override string Name => PlayerName;

    protected override string GetDefaultDescription() => $"{PlayerName} the level {Level} adventurer is here.";

    public override int MaxHP => 100 + (Level * 10);

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(2);

    protected override int RegenAmount => 1 + (Level / 5);

    /// <summary>
    /// The player's display name.
    /// </summary>
    public string PlayerName => Ctx?.State.Get<string>("player_name") ?? "Unknown";

    /// <summary>
    /// When the player last logged in.
    /// </summary>
    public DateTimeOffset LastLogin
    {
        get
        {
            var ticks = Ctx?.State.Get<long>("last_login") ?? 0;
            return ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// Time played in the current session.
    /// </summary>
    public TimeSpan SessionTime
    {
        get
        {
            if (Ctx is null) return TimeSpan.Zero;
            var loginTicks = Ctx.State.Get<long>("last_login");
            if (loginTicks <= 0) return TimeSpan.Zero;
            return Ctx.Clock.Now - new DateTimeOffset(loginTicks, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Current experience points.
    /// </summary>
    public int Experience => Ctx?.State.Get<int>("experience") ?? 0;

    /// <summary>
    /// Current level (derived from experience).
    /// </summary>
    public int Level => Ctx?.State.Get<int>("level") ?? 1;

    // IHasInventory implementation

    /// <summary>
    /// Maximum weight the player can carry. Scales with level.
    /// </summary>
    public int CarryCapacity => 100 + (Level * 10);

    /// <summary>
    /// Current total weight of carried items.
    /// Calculated from items in the player's inventory via ContainerRegistry.
    /// </summary>
    public int CarriedWeight
    {
        get
        {
            if (Ctx?.World is null) return 0;

            int totalWeight = 0;
            var contents = Ctx.World.GetRoomContents(Id);
            foreach (var itemId in contents)
            {
                var item = Ctx.World.GetObject<IItem>(itemId);
                if (item is not null)
                {
                    totalWeight += item.Weight;
                }
            }
            return totalWeight;
        }
    }

    /// <summary>
    /// Check if the player can carry an additional item of the given weight.
    /// </summary>
    public bool CanCarry(int weight)
    {
        return CarriedWeight + weight <= CarryCapacity;
    }

    // IHasEquipment implementation

    /// <summary>
    /// Total armor class from all equipped armor pieces.
    /// </summary>
    public int TotalArmorClass
    {
        get
        {
            if (Ctx?.World is null) return 0;

            int totalAC = 0;
            var equipment = Ctx.World.GetEquipment(Id);
            foreach (var kvp in equipment)
            {
                var item = Ctx.World.GetObject<IArmor>(kvp.Value);
                if (item is not null)
                {
                    totalAC += item.ArmorClass;
                }
            }
            return totalAC;
        }
    }

    /// <summary>
    /// Weapon damage range from equipped weapon(s).
    /// Returns (min, max) damage. If no weapon equipped, returns base unarmed damage (1-2).
    /// </summary>
    public (int min, int max) WeaponDamage
    {
        get
        {
            if (Ctx?.World is null) return (1, 2);

            int minDmg = 0;
            int maxDmg = 0;
            var equipment = Ctx.World.GetEquipment(Id);

            foreach (var kvp in equipment)
            {
                var weapon = Ctx.World.GetObject<IWeapon>(kvp.Value);
                if (weapon is not null)
                {
                    minDmg += weapon.MinDamage;
                    maxDmg += weapon.MaxDamage;
                }
            }

            // If no weapon equipped, return unarmed damage
            if (maxDmg == 0)
            {
                return (1, 2);
            }

            return (minDmg, maxDmg);
        }
    }

    /// <summary>
    /// Initialize or restore player state.
    /// </summary>
    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);

        // Initialize level if not set
        if (!HasStateKey(ctx, "level"))
        {
            ctx.State.Set("level", 1);
        }

        // Initialize experience if not set
        if (!HasStateKey(ctx, "experience"))
        {
            ctx.State.Set("experience", 0);
        }

        // Note: Coins are now created as objects by GameServer.CreateStartingCoinsAsync()
    }

    /// <summary>
    /// Called when the player logs in to the game.
    /// </summary>
    public virtual void OnLogin(IMudContext ctx)
    {
        // Update last login time
        ctx.State.Set("last_login", ctx.Clock.Now.Ticks);

        // Announce to room
        ctx.Emote("has entered the realm.");
    }

    /// <summary>
    /// Called when the player logs out of the game.
    /// </summary>
    public virtual void OnLogout(IMudContext ctx)
    {
        // Calculate and add session time to total playtime
        var sessionTicks = ctx.Clock.Now.Ticks - (ctx.State.Get<long>("last_login"));
        var existingPlaytime = ctx.State.Get<long>("total_playtime");
        ctx.State.Set("total_playtime", existingPlaytime + sessionTicks);

        // Announce to room
        ctx.Emote("has left the realm.");
    }

    /// <summary>
    /// Award experience points. Automatically handles level-ups.
    /// </summary>
    public virtual void AwardExperience(int amount, IMudContext ctx)
    {
        if (amount <= 0) return;

        var currentXp = ctx.State.Get<int>("experience");
        var currentLevel = ctx.State.Get<int>("level");

        currentXp += amount;
        ctx.State.Set("experience", currentXp);

        ctx.Say($"You gain {amount} experience!");

        // Check for level up
        var xpForNextLevel = CalculateXpForLevel(currentLevel + 1);
        while (currentXp >= xpForNextLevel)
        {
            currentLevel++;
            ctx.State.Set("level", currentLevel);

            // Update max HP on level up (heal the difference)
            var oldMaxHp = 100 + ((currentLevel - 1) * 10);
            var newMaxHp = MaxHP;
            var hpGain = newMaxHp - oldMaxHp;

            var currentHp = ctx.State.Get<int>("hp");
            ctx.State.Set("hp", Math.Min(currentHp + hpGain, newMaxHp));

            ctx.Say($"LEVEL UP! You are now level {currentLevel}!");
            ctx.Emote("glows briefly as they gain a level!");

            xpForNextLevel = CalculateXpForLevel(currentLevel + 1);
        }
    }

    /// <summary>
    /// Handle player death - different from NPC death.
    /// Players respawn rather than being destructed.
    /// </summary>
    public override void Die(string? killerId, IMudContext ctx)
    {
        ctx.Emote("collapses to the ground!");

        // Notify via hook
        if (this is IOnDeath onDeath)
        {
            onDeath.OnDeath(killerId, ctx);
        }

        // Players automatically resurrect after a short delay
        ctx.CallOut(nameof(Resurrect), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Resurrect the player after death.
    /// </summary>
    public virtual void Resurrect(IMudContext ctx)
    {
        // Restore to half health
        var halfHp = MaxHP / 2;
        ctx.State.Set("hp", halfHp);

        ctx.Emote("gasps and returns to life!");
        ctx.Say($"You have been resurrected with {halfHp}/{MaxHP} HP.");
    }

    /// <summary>
    /// Set the player's name. Called during character creation.
    /// </summary>
    public void SetPlayerName(string name, IMudContext ctx)
    {
        ctx.State.Set("player_name", name);
    }

    /// <summary>
    /// Get total time played across all sessions.
    /// </summary>
    public TimeSpan TotalPlaytime
    {
        get
        {
            if (Ctx is null) return TimeSpan.Zero;
            var ticks = Ctx.State.Get<long>("total_playtime");
            return TimeSpan.FromTicks(ticks) + SessionTime;
        }
    }

    /// <summary>
    /// Calculate XP required for a given level.
    /// </summary>
    private static int CalculateXpForLevel(int level)
    {
        if (level <= 1) return 0;
        return (int)(BaseXpPerLevel * Math.Pow(XpMultiplier, level - 2));
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
