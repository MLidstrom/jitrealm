# JitRealm lpMUD Evolution Plan

This document details how to evolve JitRealm from its current state into a more complete lpMUD-like system while preserving its unique C#/hot-reload advantages.

**Current Version: v0.17**

## Design Philosophy

### What Makes lpMUD Special

1. **Everything is an object** - Players, NPCs, items, rooms all share a common base
2. **Mudlib provides behavior** - Standard library classes define game mechanics
3. **Driver provides primitives** - Core functions (efuns) provided by driver
4. **Inheritance hierarchy** - `monster.c → living.c → object.c`
5. **Object containment** - Objects physically contain other objects

### JitRealm's Unique Strengths to Preserve

1. **Hot-reload** - Edit code, reload without restart
2. **C# type safety** - Interfaces over inheritance, compile-time checks
3. **State externalization** - IStateStore survives reloads
4. **Security sandboxing** - World code can't access dangerous APIs
5. **Modern async/await** - Non-blocking IO throughout

### Architectural Decision: Composition Over Inheritance

lpMUD uses deep inheritance (`player.c → living.c → object.c`). JitRealm will use **interface composition**:

```
lpMUD:     class Player : Living : Object
JitRealm:  class Player : MudObjectBase, ILiving, IPlayer, IOnLoad, IHeartbeat
```

Benefits:
- Multiple "capabilities" without diamond inheritance
- Clearer contracts via interfaces
- Hot-reload friendly (interfaces stable, implementations change)

---

## Phase Overview

| Phase | Name | Focus | Status |
|-------|------|-------|--------|
| 8 | Living Foundation | ILiving, stats, damage | ✅ Complete |
| 9 | Player as Object | Clone players from blueprint | ✅ Complete |
| 10 | Items & Inventory | IItem, get/drop, weight | ✅ Complete |
| 11 | Equipment | Slots, IEquippable, bonuses | ✅ Complete |
| 12 | Combat | Attack/defend, ICombatant | ✅ Complete |
| 13 | NPCs & AI | Monster blueprints, spawning | ✅ Complete |
| 14 | Mudlib Polish | Command registry, social commands | ✅ Complete |
| 15 | Configuration | appsettings.json, cross-platform | ✅ Complete |
| 16 | Player Accounts | Login/registration, persistent data | ✅ Complete |
| 17 | Web Frontend | WebSocket API, SvelteKit client | Pending |

---

## Phase 8: Living Foundation ✅ COMPLETE

**Goal**: Establish the concept of "living" objects with health and stats.

### New Interfaces

```csharp
// Mud/ILiving.cs
public interface ILiving : IMudObject
{
    /// <summary>Current hit points.</summary>
    int HP { get; }

    /// <summary>Maximum hit points.</summary>
    int MaxHP { get; }

    /// <summary>Whether this living is alive.</summary>
    bool IsAlive => HP > 0;

    /// <summary>Called when this living takes damage.</summary>
    void TakeDamage(int amount, string? attackerId, IMudContext ctx);

    /// <summary>Called when this living is healed.</summary>
    void Heal(int amount, IMudContext ctx);

    /// <summary>Called when HP reaches 0.</summary>
    void Die(string? killerId, IMudContext ctx);
}
```

```csharp
// Mud/IHasStats.cs (optional extension)
public interface IHasStats : ILiving
{
    int Strength { get; }
    int Dexterity { get; }
    int Constitution { get; }
    int Intelligence { get; }
    int Wisdom { get; }
    int Charisma { get; }
}
```

### New Hooks

```csharp
// Mud/Hooks.cs additions
public interface IOnDamage
{
    /// <summary>Called when about to take damage. Return modified amount.</summary>
    int OnDamage(int amount, string? attackerId, IMudContext ctx);
}

public interface IOnDeath
{
    /// <summary>Called when HP reaches 0.</summary>
    void OnDeath(string? killerId, IMudContext ctx);
}

public interface IOnHeal
{
    /// <summary>Called when healed.</summary>
    void OnHeal(int amount, IMudContext ctx);
}
```

### Standard Library Base Class

```csharp
// World/std/living.cs
using JitRealm.Mud;

/// <summary>
/// Base class for all living beings (players, NPCs, monsters).
/// Manages HP via IStateStore for persistence across reloads.
/// </summary>
public abstract class LivingBase : MudObjectBase, ILiving, IOnLoad, IHeartbeat
{
    protected IMudContext? _ctx;

    // Stats stored in IStateStore for persistence
    public int HP => _ctx?.State.Get<int>("hp") ?? 0;
    public virtual int MaxHP => 100;
    public bool IsAlive => HP > 0;

    public virtual TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(2);

    public virtual void OnLoad(IMudContext ctx)
    {
        _ctx = ctx;
        // Initialize HP if not set
        if (!ctx.State.Keys.Contains("hp"))
        {
            ctx.State.Set("hp", MaxHP);
        }
    }

    public virtual void Heartbeat(IMudContext ctx)
    {
        // Natural regeneration: 1 HP per heartbeat if alive and not at max
        if (IsAlive && HP < MaxHP)
        {
            Heal(1, ctx);
        }
    }

    public virtual void TakeDamage(int amount, string? attackerId, IMudContext ctx)
    {
        // Allow IOnDamage to modify
        if (this is IOnDamage onDamage)
        {
            amount = onDamage.OnDamage(amount, attackerId, ctx);
        }

        var newHp = Math.Max(0, HP - amount);
        ctx.State.Set("hp", newHp);

        if (newHp <= 0)
        {
            Die(attackerId, ctx);
        }
    }

    public virtual void Heal(int amount, IMudContext ctx)
    {
        var newHp = Math.Min(MaxHP, HP + amount);
        ctx.State.Set("hp", newHp);

        if (this is IOnHeal onHeal)
        {
            onHeal.OnHeal(amount, ctx);
        }
    }

    public virtual void Die(string? killerId, IMudContext ctx)
    {
        ctx.Emote("collapses to the ground!");

        if (this is IOnDeath onDeath)
        {
            onDeath.OnDeath(killerId, ctx);
        }
    }
}
```

### IMudContext Additions

```csharp
// Add to IMudContext.cs
/// <summary>Deal damage to a living object.</summary>
void DealDamage(string targetId, int amount);

/// <summary>Heal a living object.</summary>
void HealTarget(string targetId, int amount);
```

### Files to Create/Modify

| File | Action |
|------|--------|
| `Mud/ILiving.cs` | Create |
| `Mud/IHasStats.cs` | Create (optional) |
| `Mud/Hooks.cs` | Add IOnDamage, IOnDeath, IOnHeal |
| `Mud/IMudContext.cs` | Add DealDamage, HealTarget |
| `Mud/MudContext.cs` | Implement damage/heal |
| `World/std/living.cs` | Create base class |

### Acceptance Criteria ✅

- [x] ILiving interface defined with HP, MaxHP, IsAlive
- [x] LivingBase class in World/std/ compiles and loads
- [x] Heartbeat triggers regeneration
- [x] TakeDamage reduces HP, triggers OnDamage hook
- [x] HP=0 triggers Die() and OnDeath hook
- [x] HP persists across reload via IStateStore

---

## Phase 9: Player as World Object ✅ COMPLETE

**Goal**: Transform Player from a driver-side class to a cloneable world object.

### The Big Change

**Before (v0.7)**:
```
Session.Player = new Player("Alice")  // Driver-side class
```

**After**:
```
var playerId = await ObjectManager.CloneAsync<IPlayer>("std/player.cs", state);
Session.PlayerId = playerId;  // Reference to world object
```

### New Interface

```csharp
// Mud/IPlayer.cs
public interface IPlayer : ILiving
{
    /// <summary>Player's display name.</summary>
    string PlayerName { get; }

    /// <summary>When the player last logged in.</summary>
    DateTimeOffset? LastLogin { get; }

    /// <summary>Total play time.</summary>
    TimeSpan PlayTime { get; }

    /// <summary>Player's current experience points.</summary>
    int Experience { get; }

    /// <summary>Player's level.</summary>
    int Level { get; }
}
```

### Standard Player Blueprint

```csharp
// World/std/player.cs
using JitRealm.Mud;

public class PlayerObject : LivingBase, IPlayer, IOnEnter, IOnLeave
{
    public string PlayerName => _ctx?.State.Get<string>("playerName") ?? "Unknown";
    public DateTimeOffset? LastLogin => _ctx?.State.Get<DateTimeOffset?>("lastLogin");
    public TimeSpan PlayTime => TimeSpan.FromSeconds(_ctx?.State.Get<int>("playTimeSeconds") ?? 0);
    public int Experience => _ctx?.State.Get<int>("experience") ?? 0;
    public int Level => CalculateLevel(Experience);

    public override int MaxHP => 100 + (Level * 10);

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("lastLogin", ctx.Clock.Now);
    }

    public override void Heartbeat(IMudContext ctx)
    {
        base.Heartbeat(ctx);
        // Track play time
        var seconds = ctx.State.Get<int>("playTimeSeconds");
        ctx.State.Set("playTimeSeconds", seconds + 2); // Heartbeat interval
    }

    public void OnEnter(IMudContext ctx, string whoId)
    {
        if (whoId != ctx.CurrentObjectId)
        {
            var other = ctx.World.GetObject<IMudObject>(whoId);
            ctx.Tell(ctx.CurrentObjectId!, $"{other?.Name ?? whoId} arrives.");
        }
    }

    public void OnLeave(IMudContext ctx, string whoId)
    {
        if (whoId != ctx.CurrentObjectId)
        {
            var other = ctx.World.GetObject<IMudObject>(whoId);
            ctx.Tell(ctx.CurrentObjectId!, $"{other?.Name ?? whoId} leaves.");
        }
    }

    private static int CalculateLevel(int exp) => 1 + (exp / 1000);
}
```

### Session Changes

```csharp
// Mud/Network/ISession.cs changes
public interface ISession
{
    // Remove: Player? Player { get; set; }
    // Add:
    string? PlayerId { get; set; }  // Object ID of player instance

    // Helper to get player object
    IPlayer? GetPlayer(ISandboxedWorldAccess world) =>
        PlayerId is null ? null : world.GetObject<IPlayer>(PlayerId);
}
```

### Player Lifecycle

```
1. Client connects
2. Session created (no player yet)
3. Login prompt: "Enter name: "
4. Check for saved player:
   a. If exists: Load from persistence, attach to session
   b. If new: Clone std/player.cs, set playerName, attach to session
5. Move player to start room
6. On disconnect: Save player state, mark offline (don't destruct)
7. On reconnect: Find existing player instance, reattach
```

### WorldState Changes

```csharp
// Remove from WorldState.cs:
public Player? Player { get; set; }

// Players are now just instances in ObjectManager
// Single-player mode: WorldState.SinglePlayerId for backwards compat
```

### CommandLoop Changes

```csharp
// Before:
var player = _state.Player!;
var room = _state.Objects!.Get<IRoom>(player.LocationId!);

// After:
var playerId = _session.PlayerId!;
var playerLocation = _state.Containers.GetContainer(playerId);
var room = _state.Objects!.Get<IRoom>(playerLocation!);
```

### Files to Create/Modify

| File | Action |
|------|--------|
| `Mud/IPlayer.cs` | Create |
| `World/std/player.cs` | Create |
| `Mud/Network/ISession.cs` | Change Player to PlayerId |
| `Mud/Network/TelnetSession.cs` | Update for PlayerId |
| `Mud/Network/ConsoleSession.cs` | Update for PlayerId |
| `Mud/WorldState.cs` | Remove Player property |
| `Mud/CommandLoop.cs` | Use PlayerId + ContainerRegistry |
| `Mud/Network/GameServer.cs` | Update player handling |
| `Mud/Persistence/` | Update save/load for player instances |

### Acceptance Criteria ✅

- [x] Player cloned from World/std/player.cs on login
- [x] Player instance has HP, Level, Experience
- [x] Player persists between sessions (save/load)
- [x] Multiple players each have own instance
- [x] Player location via ContainerRegistry
- [x] `score` command shows player stats
- [x] Player heartbeat tracks play time

---

## Phase 10: Items & Inventory ✅ COMPLETE

**Goal**: Enable objects that can be picked up, dropped, and carried.

### New Interfaces

```csharp
// Mud/IItem.cs
public interface IItem : IMudObject
{
    /// <summary>Weight in arbitrary units.</summary>
    int Weight { get; }

    /// <summary>Value in currency units.</summary>
    int Value { get; }

    /// <summary>Short description for inventory lists.</summary>
    string ShortDescription { get; }

    /// <summary>Long description when examined.</summary>
    string LongDescription { get; }
}
```

```csharp
// Mud/ICarryable.cs
public interface ICarryable : IItem
{
    /// <summary>Called when picked up.</summary>
    void OnGet(string whoId, IMudContext ctx);

    /// <summary>Called when dropped.</summary>
    void OnDrop(string whoId, IMudContext ctx);

    /// <summary>Called when given to someone.</summary>
    void OnGive(string fromId, string toId, IMudContext ctx);
}
```

```csharp
// Mud/IContainer.cs
public interface IContainer : IItem
{
    /// <summary>Maximum weight this container can hold.</summary>
    int MaxCapacity { get; }

    /// <summary>Whether this container is open.</summary>
    bool IsOpen { get; }

    /// <summary>Open the container.</summary>
    void Open(string whoId, IMudContext ctx);

    /// <summary>Close the container.</summary>
    void Close(string whoId, IMudContext ctx);
}
```

### ILiving Inventory Extension

```csharp
// Add to ILiving.cs or create IHasInventory.cs
public interface IHasInventory : IMudObject
{
    /// <summary>Maximum weight this being can carry.</summary>
    int CarryCapacity { get; }

    /// <summary>Current carried weight.</summary>
    int CarriedWeight { get; }

    /// <summary>Check if can pick up an item.</summary>
    bool CanCarry(IItem item);
}
```

### IMudContext Additions

```csharp
// Add to IMudContext.cs
/// <summary>Move an object to a new container (room, inventory, bag).</summary>
bool Move(string objectId, string destinationId);

/// <summary>Get the current weight of objects in a container.</summary>
int GetContainerWeight(string containerId);

/// <summary>Get all items in the current object's inventory.</summary>
IEnumerable<string> GetInventory();
```

### New Commands

| Command | Action |
|---------|--------|
| `get <item>` | Pick up item from room |
| `drop <item>` | Drop item to room |
| `give <item> to <player>` | Give item to another player |
| `inventory` / `i` | List carried items |
| `examine <item>` | Show item's LongDescription |
| `put <item> in <container>` | Put item in container |
| `get <item> from <container>` | Get item from container |

### Example Item

```csharp
// World/items/sword.cs
using JitRealm.Mud;

public sealed class Sword : MudObjectBase, ICarryable
{
    public override string Name => "a steel sword";
    public string ShortDescription => "a gleaming steel sword";
    public string LongDescription => "A well-crafted steel sword with a leather-wrapped hilt. It looks sharp.";
    public int Weight => 5;
    public int Value => 100;

    public void OnGet(string whoId, IMudContext ctx)
    {
        ctx.Emote($"picks up {Name}");
    }

    public void OnDrop(string whoId, IMudContext ctx)
    {
        ctx.Emote($"drops {Name}");
    }

    public void OnGive(string fromId, string toId, IMudContext ctx)
    {
        var to = ctx.World.GetObject<IMudObject>(toId);
        ctx.Say($"Here, take this {Name}.");
    }
}
```

### Files to Create/Modify

| File | Action |
|------|--------|
| `Mud/IItem.cs` | Create |
| `Mud/ICarryable.cs` | Create |
| `Mud/IContainer.cs` | Create |
| `Mud/IHasInventory.cs` | Create |
| `Mud/IMudContext.cs` | Add Move, GetContainerWeight, GetInventory |
| `Mud/MudContext.cs` | Implement new methods |
| `Mud/CommandLoop.cs` | Add get, drop, give, inventory, examine, put commands |
| `World/items/sword.cs` | Create example item |
| `World/std/item.cs` | Create base item class |

### Acceptance Criteria ✅

- [x] Items can be cloned into rooms
- [x] `get <item>` moves item to player inventory
- [x] `drop <item>` moves item to current room
- [x] `inventory` lists carried items with weights
- [x] Weight limit enforced (can't pick up too much)
- [x] Items persist in inventories across save/load
- [x] OnGet/OnDrop hooks fire correctly

---

## Phase 11: Equipment System ✅ COMPLETE

**Goal**: Allow items to be equipped in specific slots with stat bonuses.

### New Interfaces

```csharp
// Mud/IEquippable.cs
public interface IEquippable : ICarryable
{
    /// <summary>Which slot this equips to.</summary>
    EquipmentSlot Slot { get; }

    /// <summary>Called when equipped.</summary>
    void OnEquip(string whoId, IMudContext ctx);

    /// <summary>Called when unequipped.</summary>
    void OnUnequip(string whoId, IMudContext ctx);
}

public enum EquipmentSlot
{
    Head,
    Neck,
    Body,
    Back,
    Arms,
    Hands,
    Waist,
    Legs,
    Feet,
    MainHand,
    OffHand,
    Ring1,
    Ring2
}
```

```csharp
// Mud/IWeapon.cs
public interface IWeapon : IEquippable
{
    /// <summary>Minimum damage.</summary>
    int MinDamage { get; }

    /// <summary>Maximum damage.</summary>
    int MaxDamage { get; }

    /// <summary>Weapon type (sword, axe, etc.).</summary>
    string WeaponType { get; }
}
```

```csharp
// Mud/IArmor.cs
public interface IArmor : IEquippable
{
    /// <summary>Armor class bonus.</summary>
    int ArmorClass { get; }

    /// <summary>Armor type (cloth, leather, plate).</summary>
    string ArmorType { get; }
}
```

### Equipment Registry

```csharp
// Mud/EquipmentRegistry.cs
public sealed class EquipmentRegistry
{
    // livingId -> (slot -> itemId)
    private readonly Dictionary<string, Dictionary<EquipmentSlot, string>> _equipment = new();

    public bool Equip(string livingId, EquipmentSlot slot, string itemId);
    public string? Unequip(string livingId, EquipmentSlot slot);
    public string? GetEquipped(string livingId, EquipmentSlot slot);
    public IReadOnlyDictionary<EquipmentSlot, string> GetAllEquipped(string livingId);

    // Serialization
    public EquipmentSaveData ToSerializable();
    public static EquipmentRegistry FromSerializable(EquipmentSaveData data);
}
```

### ILiving Extension

```csharp
// Add to ILiving or IHasEquipment interface
public interface IHasEquipment : ILiving
{
    /// <summary>Total armor class from equipment.</summary>
    int TotalArmorClass { get; }

    /// <summary>Equipped weapon damage range.</summary>
    (int min, int max) WeaponDamage { get; }
}
```

### New Commands

| Command | Action |
|---------|--------|
| `equip <item>` | Equip item to its slot |
| `unequip <slot>` | Unequip item from slot |
| `equipment` / `eq` | Show equipped items |
| `compare <item>` | Compare item to equipped |

### Files to Create/Modify

| File | Action |
|------|--------|
| `Mud/IEquippable.cs` | Create |
| `Mud/IWeapon.cs` | Create |
| `Mud/IArmor.cs` | Create |
| `Mud/EquipmentRegistry.cs` | Create |
| `Mud/WorldState.cs` | Add EquipmentRegistry |
| `Mud/CommandLoop.cs` | Add equip, unequip, equipment commands |
| `Mud/Persistence/` | Save/load equipment |
| `World/items/armor/` | Create example armor |
| `World/items/weapons/` | Create example weapons |

### Acceptance Criteria ✅

- [x] Items with IEquippable can be equipped
- [x] Only one item per slot
- [x] `equipment` shows all equipped items
- [x] Equipped weapon affects damage calculation
- [x] Equipped armor affects damage reduction
- [x] Equipment persists across save/load

---

## Phase 12: Combat System ✅ COMPLETE

**Goal**: Enable players and NPCs to fight each other.

### New Interfaces ✅

```csharp
// Mud/ICombatant.cs
public interface ICombatant : ILiving
{
    /// <summary>Attack a target.</summary>
    void Attack(string targetId, IMudContext ctx);

    /// <summary>Whether currently in combat.</summary>
    bool InCombat { get; }

    /// <summary>Current combat target.</summary>
    string? CombatTarget { get; }

    /// <summary>Stop fighting.</summary>
    void StopCombat(IMudContext ctx);

    /// <summary>Attempt to flee combat.</summary>
    bool TryFlee(IMudContext ctx);
}
```

### Combat Hooks ✅

```csharp
// Add to Hooks.cs
public interface IOnAttack
{
    /// <summary>Called when attacking. Return modified damage.</summary>
    int OnAttack(string targetId, int baseDamage, IMudContext ctx);
}

public interface IOnDefend
{
    /// <summary>Called when defending. Return modified damage taken.</summary>
    int OnDefend(string attackerId, int incomingDamage, IMudContext ctx);
}

public interface IOnKill
{
    /// <summary>Called when this object kills something.</summary>
    void OnKill(string victimId, IMudContext ctx);
}
```

### Combat Scheduler ✅

```csharp
// Mud/CombatScheduler.cs
public sealed class CombatScheduler
{
    // Tracks active combats
    // Combat rounds process every 3 seconds
    // Damage = weapon + OnAttack - armor - OnDefend (minimum 1)

    public void StartCombat(string attackerId, string defenderId);
    public void EndCombat(string combatantId);
    public IEnumerable<(string attacker, string defender)> GetActiveCombats();
    public void ProcessCombatRounds(WorldState state, IClock clock);
    public bool AttemptFlee(string combatantId, WorldState state, IClock clock);
}
```

### Combat Flow ✅

```
1. Player: "kill goblin"
2. CombatScheduler.StartCombat(playerId, goblinId)
3. Each game loop tick (3-second rounds):
   a. For each active combat:
      - Calculate attacker damage (weapon + OnAttack)
      - Calculate defender mitigation (armor + OnDefend)
      - Apply damage via TakeDamage() (minimum 1 damage)
      - Send combat messages to attacker, defender, room
   b. If defender HP <= 0:
      - End combat
      - Award experience (victim's MaxHP)
      - Call OnDeath, OnKill hooks
4. Player: "flee" - 50% success, moves to random exit
```

### New Commands ✅

| Command | Action |
|---------|--------|
| `kill <target>` / `attack <target>` | Start combat |
| `flee` / `retreat` | Attempt to escape combat (50% chance) |
| `consider <target>` / `con` | Estimate target difficulty |

### Files Created/Modified ✅

| File | Action |
|------|--------|
| `Mud/ICombatant.cs` | Created |
| `Mud/CombatScheduler.cs` | Created |
| `Mud/Hooks.cs` | Added IOnAttack, IOnDefend, IOnKill |
| `Mud/WorldState.cs` | Added CombatScheduler, CreateContext helper |
| `Mud/CommandLoop.cs` | Added kill, flee, consider commands |
| `Mud/Network/GameServer.cs` | Added combat commands, ProcessCombat |
| `World/npcs/goblin.cs` | Created test combat target |
| `World/Rooms/meadow.cs` | Updated description |

### Acceptance Criteria ✅

- [x] `kill goblin` starts combat
- [x] Combat rounds process automatically
- [x] Damage calculated from weapon + stats
- [x] Armor reduces incoming damage
- [x] Death ends combat, awards experience
- [x] `flee` has chance to escape
- [x] Combat messages sent to room

---

## Phase 13: NPCs & AI ✅ COMPLETE

**Goal**: Populate the world with interactive non-player characters.

### Monster Base Class ✅

```csharp
// World/std/monster.cs
public abstract class MonsterBase : LivingBase, IOnEnter, IOnDeath, IHasEquipment
{
    public virtual int ExperienceValue => MaxHP;
    public virtual bool IsAggressive => true;
    public virtual int AggroDelaySeconds => 2;
    public virtual int RespawnDelaySeconds => 60;
    public virtual double WanderChance => 0.0;
    public virtual int TotalArmorClass => 0;
    public virtual (int min, int max) WeaponDamage => (1, 3);

    public virtual void OnEnter(IMudContext ctx, string whoId)
    {
        if (!IsAlive || !IsAggressive) return;

        var entering = ctx.World.GetObject<IPlayer>(whoId);
        if (entering is null) return;

        ctx.Emote($"snarls at {entering.Name}!");
        ctx.CallOut(nameof(StartAttack), TimeSpan.FromSeconds(AggroDelaySeconds), whoId);
    }

    public void StartAttack(IMudContext ctx, string targetId)
    {
        if (!IsAlive) return;
        var myRoom = ctx.World.GetObjectLocation(Id);
        var targetRoom = ctx.World.GetObjectLocation(targetId);
        if (myRoom != targetRoom) return;

        var target = ctx.World.GetObject<ILiving>(targetId);
        if (target is null || !target.IsAlive) return;

        ctx.Emote($"attacks {target.Name}!");
    }

    public virtual void OnDeath(string? killerId, IMudContext ctx)
    {
        ctx.Emote("lets out a dying shriek!");
        ctx.CallOut(nameof(Respawn), TimeSpan.FromSeconds(RespawnDelaySeconds));
    }

    public virtual void Respawn(IMudContext ctx)
    {
        FullHeal(ctx);
        ctx.Emote("emerges from the shadows!");
    }

    // TryWander in Heartbeat for AI behavior
}
```

### NPC Base Class ✅

```csharp
// World/std/npc.cs
public abstract class NPCBase : LivingBase, IOnEnter
{
    public override int MaxHP => 1000;  // High HP - hard to kill
    protected override int RegenAmount => 10;  // Fast regen

    public virtual string? GetGreeting(IPlayer player) => null;

    public virtual void OnEnter(IMudContext ctx, string whoId)
    {
        if (!IsAlive) return;
        var entering = ctx.World.GetObject<IPlayer>(whoId);
        if (entering is null) return;

        var greeting = GetGreeting(entering);
        if (!string.IsNullOrEmpty(greeting))
            ctx.Say(greeting);
    }

    public override void Die(string? killerId, IMudContext ctx)
    {
        ctx.Emote("staggers but remains standing!");
        FullHeal(ctx);  // NPCs don't really die
    }
}
```

### Spawn System ✅

```csharp
// Mud/ISpawner.cs
public interface ISpawner
{
    IReadOnlyDictionary<string, int> Spawns { get; }
    void Respawn(IMudContext ctx);
}

// WorldState.ProcessSpawnsAsync() tracks and replenishes spawns
// Called when entering rooms and on room load
```

### Files Created/Modified ✅

| File | Action |
|------|--------|
| `Mud/ISpawner.cs` | Created |
| `World/std/monster.cs` | Created |
| `World/std/npc.cs` | Created |
| `World/npcs/goblin.cs` | Updated to extend MonsterBase |
| `World/npcs/shopkeeper.cs` | Created |
| `World/Rooms/meadow.cs` | Added ISpawner (spawns goblin) |
| `World/Rooms/shop.cs` | Created with ISpawner (spawns shopkeeper) |
| `World/Rooms/start.cs` | Added east exit to shop |
| `Mud/WorldState.cs` | Added ProcessSpawnsAsync helper |
| `Mud/CommandLoop.cs` | Spawn processing on room enter |
| `Mud/Network/GameServer.cs` | Spawn processing for multiplayer |

### Acceptance Criteria ✅

- [x] Monsters spawn in rooms (meadow spawns goblin)
- [x] Aggressive monsters attack players (via OnEnter + CallOut)
- [x] Monsters use Heartbeat for AI (wander behavior)
- [x] Dead monsters respawn after delay (via CallOut)
- [x] Experience awarded on kill (via combat system)
- [x] Friendly NPCs can talk (shopkeeper greets players)

---

## Phase 14: Mudlib Polish ✅ COMPLETE

**Goal**: Create a complete standard library and improve command handling.

### Standard Library Structure

```
World/
├── std/
│   ├── object.cs      # MudObjectBase equivalent (exists)
│   ├── living.cs      # Base for all living things
│   ├── player.cs      # Player blueprint
│   ├── monster.cs     # Monster blueprint
│   ├── npc.cs         # Non-combat NPC base
│   ├── room.cs        # Room base class
│   ├── item.cs        # Item base class
│   ├── weapon.cs      # Weapon base class
│   ├── armor.cs       # Armor base class
│   └── container.cs   # Container base class
├── rooms/
│   ├── start.cs       # Starting room
│   └── meadow.cs      # Example room
├── items/
│   ├── weapons/
│   │   └── sword.cs
│   └── armor/
│       └── helmet.cs
└── npcs/
    ├── goblin.cs
    └── shopkeeper.cs
```

### Command Dispatch Improvements

```csharp
// Mud/Commands/ICommand.cs
public interface ICommand
{
    string Name { get; }
    string[] Aliases { get; }
    string Usage { get; }
    string Description { get; }

    Task ExecuteAsync(CommandContext ctx);
}

// Mud/Commands/CommandRegistry.cs
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new();

    public void Register(ICommand command);
    public ICommand? Find(string input);
}
```

### Social Commands

| Command | Action |
|---------|--------|
| `say <message>` | Speak to room (exists) |
| `shout <message>` | Speak to adjacent rooms |
| `whisper <player> <message>` | Private message |
| `emote <action>` | Custom emote |
| `bow`, `wave`, `laugh`, etc. | Pre-defined emotes |

### Utility Commands

| Command | Action |
|---------|--------|
| `help [command]` | Show help |
| `score` | Show player stats |
| `time` | Show game time |
| `save` | Manual save (exists) |
| `quit` | Disconnect (exists) |

### Files to Create/Modify

| File | Action |
|------|--------|
| `Mud/Commands/` | Create directory |
| `Mud/Commands/ICommand.cs` | Create |
| `Mud/Commands/CommandRegistry.cs` | Create |
| `Mud/Commands/*.cs` | Individual command classes |
| `World/std/*.cs` | Complete standard library |

---

## Implementation Priority

### Must Have (Core lpMUD Feel) ✅
1. Phase 8: Living Foundation ✅
2. Phase 9: Player as World Object ✅
3. Phase 10: Items & Inventory ✅
4. Phase 13: NPCs (basic monsters) ✅

### Should Have (Complete Experience) ✅
5. Phase 11: Equipment ✅
6. Phase 12: Combat ✅
7. Phase 14: Mudlib Polish ✅
8. Phase 15: Configuration ✅
9. Phase 16: Player Accounts ✅

### Next Up
10. Phase 17: Web Frontend

### Nice to Have (Future)
- Spell/magic system
- Quest system
- Crafting
- Guilds/classes
- Areas/zones
- World builder tools

---

## Migration Notes

### Breaking Changes

1. **Player class removal** - Sessions use PlayerId string
2. **WorldState.Player removal** - Use ObjectManager for player instances
3. **Command parsing changes** - New command dispatch system

### Backwards Compatibility

- Existing rooms continue to work
- IRoom interface unchanged
- Hook interfaces additive (new hooks don't break old code)
- Save format versioned for migration

### Testing Strategy

- Unit tests for new registries
- Integration tests for combat flow
- Manual testing with sample world

---

## Success Criteria

When complete, JitRealm should support:

1. **Player creation**: Clone player blueprint, persist stats
2. **Combat**: Kill monsters, gain experience, level up
3. **Items**: Pick up loot, equip weapons/armor
4. **NPCs**: Monsters with AI, shopkeepers
5. **Persistence**: Full world state saved/restored
6. **Hot reload**: All of above survives code reload

This brings JitRealm to feature parity with a basic lpMUD while maintaining its C#/hot-reload advantages.
