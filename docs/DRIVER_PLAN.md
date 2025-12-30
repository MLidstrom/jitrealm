# JitRealm Driver Improvement Plan

This document is the implementation plan for evolving **JitRealm** from a minimal demo into a robust, lpMUD-inspired driver.

## Design goals

- **Blueprint vs instance**: source file = blueprint; runtime objects = clones with state
- **Hot reload without losing state**: reload swaps code while keeping instance state
- **Clear driver boundaries**: driver handles lifecycle/scheduling/routing/persistence/security; world code handles behavior/content
- **Security-first for networking**: treat world code as untrusted if exposed

---

## Milestone v0.2: Clones + state ✅ COMPLETE

### Deliverables

1. **Driver-assigned object identity** ✅
   - Introduced `MudObjectBase : IMudObject` with `Id` set by the driver (internal set)
   - Stable IDs: normalized paths for blueprints; `#NNNNNN` for clones

2. **Blueprint & instance model** ✅
   - `BlueprintHandle` holds compiled assembly/type/ALC and metadata (mtime)
   - `InstanceHandle` holds runtime instance + `IStateStore` + ref to blueprint

3. **State externalization** ✅
   - `IMudContext` implemented via `MudContext` class
   - `IStateStore` per instance via `DictionaryStateStore`
   - State preserved across blueprint reloads

4. **Room contents driven by driver containers** ✅
   - `ContainerRegistry` in `WorldState` manages containerId -> members
   - `look` command resolves contents from registry

5. **New commands** ✅
   - `clone <blueprintId>` — creates instance like `Rooms/meadow.cs#000001`
   - `destruct <objectId>` — removes instance
   - `stat <id>` — shows blueprint/instance info
   - `blueprints` — lists loaded blueprints

### Acceptance criteria ✅

- `clone Rooms/meadow.cs` produces unique runtime object id ✅
- Reloading a **blueprint** does not crash the driver ✅
- The driver stores per-instance state independently of the world object instance ✅

---

## Phase 2 — Driver hooks + messaging ✅ COMPLETE

### Hook interfaces ✅

- `IOnLoad` → `OnLoad(IMudContext ctx)` — wired in ObjectManager ✅
- `IOnEnter` → `OnEnter(IMudContext ctx, string whoId)` — called when player enters room ✅
- `IOnLeave` → `OnLeave(IMudContext ctx, string whoId)` — called when player leaves room ✅
- `IHeartbeat` → `HeartbeatInterval` + `Heartbeat(IMudContext ctx)` — scheduled periodic tick ✅
- `IResettable` → `Reset(IMudContext ctx)` — triggered via `reset` command ✅

### Messaging primitives ✅

- `Tell(targetId, msg)` — private message via IMudContext ✅
- `Say(msg)` — room broadcast via IMudContext ✅
- `Emote(action)` — room emote via IMudContext ✅

### Implementation details

- `MessageQueue` in WorldState — thread-safe queue for messages
- `HeartbeatScheduler` in WorldState — tracks next fire time per object
- CommandLoop processes heartbeats and drains messages each iteration

---

## Phase 3 — Hot reload with state rebinding ✅ COMPLETE

### Preferred approach ✅

**State-external instances** (driver holds state store). On reload:

1. Compile new blueprint version ✅
2. Create new object instance ✅
3. Attach existing `IStateStore` ✅
4. Call optional `IOnReload.OnReload(IMudContext ctx, string oldTypeName)` ✅
5. Swap instance handle ✅

### Implementation details

- `IOnReload` interface added to Hooks.cs
- `ReloadBlueprintAsync` checks for IOnReload first, then IOnLoad, then Create
- Old type name passed to allow custom migration logic

### Unload safety ✅

Only unload old ALC when:
- no instances reference it ✅
- no scheduled callbacks reference its types (N/A until Phase 4)

---

## Phase 4 — Scheduling & callouts ✅ COMPLETE

### Implementation ✅

- `CallOut(methodName, delay, args...)` — schedule one-time delayed call ✅
- `Every(methodName, interval, args...)` — schedule repeating call ✅
- `CancelCallOut(calloutId)` — cancel a scheduled callout ✅
- `CallOutScheduler` — priority queue for scheduled calls ✅
- CommandLoop processes due callouts each iteration ✅
- Callouts cancelled automatically on destruct/unload ✅

### Details

- Methods invoked via reflection
- First parameter can be `IMudContext` (injected automatically)
- Additional args passed from schedule call
- Repeating callouts re-schedule after each execution

---

## Phase 5 — Persistence ✅ COMPLETE

### Implementation ✅

- `IPersistenceProvider` interface for storage abstraction ✅
- `JsonPersistenceProvider` for JSON file storage ✅
- `WorldStatePersistence` service for coordinating save/load ✅
- `save` command — persist current world state ✅
- `load` command — restore from saved state ✅
- Automatic load on startup if save file exists ✅

### What gets persisted ✅

- Player state (name, location)
- All loaded instances with their state stores
- Container registry (room contents, inventories)

### Technical details

- Save data stored in `save/world.json`
- Atomic writes via temp file + rename
- Version field for future schema migrations
- IStateStore data serialized as JSON elements

---

## Phase 6 — Multi-user networking ✅ COMPLETE

### Implementation ✅

- `ISession` abstraction for connection types ✅
- `ConsoleSession` for single-player console mode ✅
- `TelnetSession` for TCP clients ✅
- `TelnetServer` for accepting connections ✅
- `SessionManager` for tracking active sessions ✅
- `GameServer` for multi-player game loop ✅

### Features ✅

- Telnet-compatible TCP server on configurable port (default 4000)
- Multiple concurrent players
- Players see others in same room
- `say` command for room chat
- `who` command to list online players
- Automatic player creation on connect

### Technical details

- Single-threaded game loop with async IO
- Non-blocking input polling per session
- Message routing to sessions by room/player
- Graceful shutdown via Ctrl+C

---

## Phase 7 — Security sandboxing ✅ COMPLETE

### Implementation ✅

Defense-in-depth security with multiple layers:

1. **Restricted assembly references** ✅
   - `SafeReferences.cs` provides curated list of safe assemblies
   - Only essential .NET assemblies allowed (System.Runtime, System.Collections, System.Linq, etc.)
   - Dangerous assemblies blocked (System.IO, System.Net, System.Diagnostics, etc.)

2. **Namespace/type blocking** ✅
   - `ForbiddenSymbolValidator.cs` uses Roslyn semantic analysis
   - Rejects code using forbidden namespaces at compile time
   - Clear error messages for blocked code

3. **API surface isolation** ✅
   - `ISandboxedWorldAccess` replaces direct `WorldState` access
   - World code can only query objects, not modify ObjectManager
   - SessionManager, schedulers not exposed to world code

4. **Execution timeouts** ✅
   - `SafeInvoker.cs` wraps all hook invocations
   - 5-second timeout for hooks and heartbeats
   - 10-second timeout for callouts

### Blocked namespaces

- System.IO, System.Net, System.Net.Http, System.Net.Sockets
- System.Diagnostics, System.Reflection.Emit
- System.Runtime.InteropServices, Microsoft.CodeAnalysis

### Blocked types

- System.Environment, System.AppDomain, System.Activator
- System.Type, System.Reflection.Assembly, System.Reflection.MethodInfo
- System.Threading.Thread, System.Threading.ThreadPool

### What world code CAN do

- `IMudContext.Tell()`, `Say()`, `Emote()` — messaging
- `IMudContext.State` — per-instance state storage
- `IMudContext.CallOut()`, `Every()`, `CancelCallOut()` — scheduling
- `IMudContext.World.GetObject<T>()` — read-only object queries
- Standard C# collections, LINQ, basic types

### Known limitations

- Cannot forcibly abort managed threads (timeouts throw exceptions)
- In-process sandboxing only (true isolation would require out-of-process worker)

---

# lpMUD Evolution Phases

The following phases evolve JitRealm toward full lpMUD feature parity.
See [LPMUD_EVOLUTION_PLAN.md](LPMUD_EVOLUTION_PLAN.md) for detailed specifications.

---

## Phase 8 — Living Foundation ✅ COMPLETE

**Goal**: Establish "living" objects with health, stats, and damage mechanics.

### New interfaces ✅

- `ILiving` — HP, MaxHP, IsAlive, TakeDamage(), Heal(), Die() ✅
- `IHasStats` (optional) — Strength, Dexterity, Constitution, etc. ✅

### New hooks ✅

- `IOnDamage` — modify incoming damage ✅
- `IOnDeath` — triggered when HP reaches 0 ✅
- `IOnHeal` — triggered when healed ✅

### Standard library ✅

- `World/std/living.cs` — base class for all living beings ✅
- HP stored in IStateStore for persistence ✅
- Heartbeat triggers natural regeneration ✅

### IMudContext additions ✅

- `DealDamage(targetId, amount)` — deal damage to a living ✅
- `HealTarget(targetId, amount)` — heal a living ✅

### Acceptance criteria ✅

- [x] ILiving interface with HP/MaxHP
- [x] LivingBase class compiles and loads
- [x] TakeDamage reduces HP, triggers OnDamage
- [x] HP=0 triggers Die() and OnDeath
- [x] Heartbeat regenerates HP
- [x] HP persists across reload

---

## Phase 9 — Player as World Object ✅ COMPLETE

**Goal**: Transform Player from driver-side class to cloneable world object.

### The big change ✅

```
Before:  Session.Player = new Player("Alice")     // Driver class
After:   Session.PlayerId = clonedObjectId        // World object
```

### New interface ✅

- `IPlayer : ILiving` — PlayerName, LastLogin, SessionTime, Experience, Level

### Standard library ✅

- `World/std/player.cs` — PlayerBase class extending LivingBase
- Stats, experience, level stored in IStateStore
- Login/logout hooks, resurrection after death, level-up system

### Session changes ✅

- `ISession.PlayerId` and `PlayerName` replace `Player` property
- Player cloned on login, location tracked via ContainerRegistry
- Session save data stores PlayerId for reconnection

### Files modified ✅

- `Mud/IPlayer.cs` — new interface
- `World/std/player.cs` — new PlayerBase blueprint
- `Mud/Network/ISession.cs` — PlayerId/PlayerName instead of Player
- `Mud/Network/ConsoleSession.cs` — updated for new ISession
- `Mud/Network/TelnetSession.cs` — updated for new ISession
- `Mud/Network/SessionManager.cs` — GetSessionsInRoom with location lookup
- `Mud/Network/GameServer.cs` — PlayerId-based operations
- `Mud/WorldState.cs` — removed Player property
- `Mud/CommandLoop.cs` — PlayerId + ContainerRegistry for location
- `Mud/MudContext.cs` — ContainerRegistry for Say/Emote room lookup
- `Mud/ContainerRegistry.cs` — added Move method
- `Mud/Persistence/SaveData.cs` — SessionSaveData replaces PlayerSaveData
- `Mud/Persistence/WorldStatePersistence.cs` — ISession parameter support
- `Mud/Player.cs` — deleted (no longer needed)

### Acceptance criteria ✅

- [x] Player cloned from World/std/player.cs
- [x] Player has HP, Level, Experience
- [x] Player persists between sessions
- [x] Multiple players have separate instances
- [x] Player location via ContainerRegistry

---

## Phase 10 — Items & Inventory ✅ COMPLETE

**Goal**: Enable objects that can be picked up, dropped, and carried.

### New interfaces ✅

- `IItem` — Weight, Value, ShortDescription, LongDescription ✅
- `ICarryable : IItem` — OnGet(), OnDrop(), OnGive() ✅
- `IContainer : IItem` — MaxCapacity, IsOpen, Open(), Close() ✅
- `IHasInventory` — CarryCapacity, CarriedWeight, CanCarry() ✅

### IMudContext additions ✅

- `Move(objectId, destinationId)` — move object between containers ✅
- `GetContainerWeight(containerId)` — total weight in container ✅
- `GetInventory()` — items in current object's inventory ✅
- `FindItem(name, containerId)` — find item by name ✅

### IStateStore additions ✅

- `Has(key)` — check if state key exists ✅

### New commands ✅

- `get <item>` / `take <item>` — pick up from room ✅
- `drop <item>` — drop to room ✅
- `inventory` / `inv` / `i` — list carried items ✅
- `examine <item>` / `exam` / `x` — show LongDescription ✅

### Standard library ✅

- `World/std/item.cs` — ItemBase and ContainerBase classes ✅
- `World/Items/rusty_sword.cs` — example item ✅
- `World/Items/health_potion.cs` — example item ✅

### Files created/modified ✅

- `Mud/IItem.cs` — new interfaces
- `World/std/item.cs` — ItemBase, ContainerBase
- `Mud/IPlayer.cs` — now extends IHasInventory
- `World/std/player.cs` — implements CarryCapacity, CarriedWeight, CanCarry
- `Mud/IMudContext.cs` — Move, GetContainerWeight, GetInventory, FindItem
- `Mud/MudContext.cs` — implementations
- `Mud/IStateStore.cs` — added Has method
- `Mud/DictionaryStateStore.cs` — implemented Has
- `Mud/CommandLoop.cs` — item commands
- `Mud/Network/GameServer.cs` — multiplayer item commands

### Acceptance criteria ✅

- [x] Items can be cloned into rooms
- [x] `get` moves item to player inventory
- [x] `drop` moves item to current room
- [x] `inventory` lists carried items with weights
- [x] Weight limit enforced
- [x] Items persist in inventories

---

## Phase 11 — Equipment System ✅ COMPLETE

**Goal**: Allow items to be equipped in slots with stat bonuses.

### New interfaces ✅

- `IEquippable : ICarryable` — Slot, OnEquip(), OnUnequip() ✅
- `IWeapon : IEquippable` — MinDamage, MaxDamage, WeaponType ✅
- `IArmor : IEquippable` — ArmorClass, ArmorType ✅
- `IHasEquipment : ILiving` — TotalArmorClass, WeaponDamage ✅

### Equipment slots ✅

```csharp
enum EquipmentSlot { Head, Neck, Body, Back, Arms, Hands,
                     Waist, Legs, Feet, MainHand, OffHand, Ring1, Ring2 }
```

### New registry ✅

- `EquipmentRegistry` — tracks livingId → (slot → itemId) ✅
- Serialization support for persistence ✅

### New commands ✅

- `equip <item>` / `wield` / `wear` — equip to appropriate slot ✅
- `unequip <slot>` / `remove` — remove from slot ✅
- `equipment` / `eq` — show equipped items with stats ✅

### Standard library ✅

- `World/std/weapon.cs` — WeaponBase class ✅
- `World/std/armor.cs` — ArmorBase, HelmetBase, ChestArmorBase, etc. ✅

### Files created/modified ✅

- `Mud/IEquippable.cs` — new interfaces
- `Mud/EquipmentRegistry.cs` — equipment tracking
- `Mud/WorldState.cs` — added EquipmentRegistry
- `Mud/Persistence/SaveData.cs` — EquipmentSaveData
- `Mud/Persistence/WorldStatePersistence.cs` — save/load equipment
- `Mud/Security/ISandboxedWorldAccess.cs` — GetEquipment, GetEquippedInSlot
- `Mud/Security/SandboxedWorldAccess.cs` — implementations
- `Mud/IPlayer.cs` — added IHasEquipment
- `World/std/player.cs` — TotalArmorClass, WeaponDamage
- `Mud/CommandLoop.cs` — equip/unequip/equipment commands
- `Mud/Network/GameServer.cs` — multiplayer equipment commands
- `World/Items/rusty_sword.cs` — updated to WeaponBase
- `World/Items/leather_vest.cs` — new armor item
- `World/Items/iron_helm.cs` — new helmet item

### Acceptance criteria ✅

- [x] IEquippable items can be equipped
- [x] One item per slot enforced
- [x] Equipped weapon affects damage (WeaponDamage property)
- [x] Equipped armor affects defense (TotalArmorClass property)
- [x] Equipment persists across save/load

---

## Phase 12 — Combat System

**Goal**: Enable players and NPCs to fight.

### New interface

- `ICombatant : ILiving` — Attack(), InCombat, CombatTarget, StopCombat()

### New hooks

- `IOnAttack` — modify outgoing damage
- `IOnDefend` — modify incoming damage
- `IOnKill` — triggered when killing something

### Combat scheduler

- `CombatScheduler` — tracks active combats
- Combat rounds processed each game loop tick
- Damage = weapon + stats + OnAttack - armor - OnDefend

### Combat flow

1. `kill <target>` starts combat
2. Each tick: calculate damage, apply, send messages
3. HP ≤ 0: end combat, award experience, trigger OnDeath/OnKill
4. `flee` attempts escape

### New commands

- `kill <target>` / `attack <target>` — start combat
- `flee` / `retreat` — attempt escape
- `consider <target>` — estimate difficulty

### Acceptance criteria

- [ ] `kill goblin` starts combat
- [ ] Combat rounds process automatically
- [ ] Damage uses weapon + stats
- [ ] Armor reduces damage
- [ ] Death ends combat, awards XP
- [ ] `flee` can escape

---

## Phase 13 — NPCs & AI

**Goal**: Populate the world with monsters and NPCs.

### Standard library

- `World/std/monster.cs` — MonsterBase class
  - ExperienceValue, IsAggressive
  - AI via Heartbeat (patrol, hunt)
  - Respawn after death via CallOut

- `World/std/npc.cs` — non-combat NPC base
  - Shopkeepers, quest givers, etc.

### Spawn system

- `ISpawner` interface — rooms can spawn NPCs
- Spawns dict: blueprintId → count
- Respawn() called periodically

### Example NPCs

- `World/npcs/goblin.cs` — aggressive monster
- `World/npcs/shopkeeper.cs` — friendly NPC

### Acceptance criteria

- [ ] Monsters spawn in rooms
- [ ] Aggressive monsters attack players
- [ ] Monster AI via Heartbeat
- [ ] Dead monsters respawn
- [ ] Experience awarded on kill
- [ ] Friendly NPCs can talk

---

## Phase 14 — Mudlib Polish

**Goal**: Complete standard library and command system.

### Standard library structure

```
World/std/
├── living.cs      # Base for living things
├── player.cs      # Player blueprint
├── monster.cs     # Monster blueprint
├── npc.cs         # Non-combat NPC
├── room.cs        # Room base class
├── item.cs        # Item base
├── weapon.cs      # Weapon base
├── armor.cs       # Armor base
└── container.cs   # Container base
```

### Command dispatch

- `ICommand` interface — Name, Aliases, Usage, Description, ExecuteAsync()
- `CommandRegistry` — register and lookup commands

### Social commands

- `shout <message>` — speak to adjacent rooms
- `whisper <player> <message>` — private message
- Pre-defined emotes: `bow`, `wave`, `laugh`, etc.

### Utility commands

- `help [command]` — show help
- `score` — show player stats
- `time` — show game time

### Acceptance criteria

- [ ] Full World/std/ library
- [ ] Command registry with help
- [ ] Social commands work
- [ ] Score shows all player stats

---

## Implementation Priority

### Core lpMUD Feel (do first)
- Phase 8: Living Foundation
- Phase 9: Player as World Object
- Phase 10: Items & Inventory
- Phase 13: NPCs (basic)

### Complete Experience (do next)
- Phase 11: Equipment
- Phase 12: Combat
- Phase 14: Mudlib Polish

### Future Enhancements
- Spell/magic system
- Quest system
- Crafting
- Guilds/classes
- World builder tools
