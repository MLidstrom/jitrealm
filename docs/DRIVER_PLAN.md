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
  - `Aliases` — alternative names for player targeting (e.g., "barnaby", "keeper") ✅
  - `ShortDescription` — display name with article (e.g., "a shopkeeper") ✅
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

## Phase 12 — Combat System ✅ COMPLETE

**Goal**: Enable players and NPCs to fight.

### New interface ✅

- `ICombatant : ILiving` — Attack(), InCombat, CombatTarget, StopCombat(), TryFlee() ✅

### New hooks ✅

- `IOnAttack` — modify outgoing damage ✅
- `IOnDefend` — modify incoming damage ✅
- `IOnKill` — triggered when killing something ✅

### Combat scheduler ✅

- `CombatScheduler` — tracks active combats ✅
- Combat rounds processed each game loop tick (3-second intervals) ✅
- Damage = weapon + OnAttack - armor - OnDefend (minimum 1) ✅

### Combat flow ✅

1. `kill <target>` starts combat ✅
2. Each tick: calculate damage, apply, send messages ✅
3. HP ≤ 0: end combat, award experience, trigger OnDeath/OnKill ✅
4. `flee` attempts escape (50% success, moves to random exit) ✅

### New commands ✅

- `kill <target>` / `attack <target>` — start combat ✅
- `flee` / `retreat` — attempt escape ✅
- `consider <target>` / `con` — estimate difficulty ✅

### Files created/modified ✅

- `Mud/ICombatant.cs` — new interface
- `Mud/CombatScheduler.cs` — combat tracking and round processing
- `Mud/Hooks.cs` — IOnAttack, IOnDefend, IOnKill hooks
- `Mud/WorldState.cs` — added CombatScheduler and CreateContext helper
- `Mud/CommandLoop.cs` — combat commands
- `Mud/Network/GameServer.cs` — multiplayer combat commands
- `World/npcs/goblin.cs` — test combat target with respawn

### Acceptance criteria ✅

- [x] `kill goblin` starts combat
- [x] Combat rounds process automatically
- [x] Damage uses weapon + stats
- [x] Armor reduces damage
- [x] Death ends combat, awards XP
- [x] `flee` can escape

---

## Phase 13 — NPCs & AI ✅ COMPLETE

**Goal**: Populate the world with monsters and NPCs.

### Standard library ✅

- `World/std/monster.cs` — MonsterBase class ✅
  - ExperienceValue, IsAggressive, AggroDelaySeconds, RespawnDelaySeconds
  - AI via Heartbeat (wander behavior)
  - Auto-attack players on sight via OnEnter + CallOut
  - Respawn after death via CallOut

- `World/std/npc.cs` — non-combat NPC base ✅
  - High HP, fast regeneration
  - Greeting system via GetGreeting()
  - NPCs don't really die (full heal on damage)

### Spawn system ✅

- `ISpawner` interface — rooms can spawn NPCs ✅
- Spawns dict: blueprintId → count ✅
- ProcessSpawnsAsync() in WorldState tracks and replenishes NPCs ✅
- Called when entering rooms and on room load ✅

### Example NPCs ✅

- `World/npcs/goblin.cs` — aggressive monster (extends MonsterBase) ✅
- `World/npcs/shopkeeper.cs` — friendly NPC (extends NPCBase) ✅

### Example spawn rooms ✅

- `World/Rooms/meadow.cs` — spawns 1 goblin (implements ISpawner) ✅
- `World/Rooms/shop.cs` — spawns 1 shopkeeper (implements ISpawner) ✅

### Files created/modified ✅

- `Mud/ISpawner.cs` — new interface with Spawns dict and Respawn method
- `World/std/monster.cs` — MonsterBase class
- `World/std/npc.cs` — NPCBase class
- `World/npcs/goblin.cs` — updated to extend MonsterBase
- `World/npcs/shopkeeper.cs` — new friendly NPC
- `World/Rooms/meadow.cs` — added ISpawner implementation
- `World/Rooms/shop.cs` — new room with shopkeeper spawn
- `World/Rooms/start.cs` — added exit to shop
- `Mud/WorldState.cs` — added ProcessSpawnsAsync helper
- `Mud/CommandLoop.cs` — spawn processing on room enter/load
- `Mud/Network/GameServer.cs` — spawn processing for multiplayer

### Acceptance criteria ✅

- [x] Monsters spawn in rooms
- [x] Aggressive monsters attack players
- [x] Monster AI via Heartbeat (wander)
- [x] Dead monsters respawn
- [x] Experience awarded on kill
- [x] Friendly NPCs can talk

---

## Phase 14 — Mudlib Polish ✅ COMPLETE

**Goal**: Complete standard library and command system.

### Standard library structure ✅

```
World/std/
├── living.cs      # Base for living things
├── player.cs      # Player blueprint
├── monster.cs     # Monster blueprint
├── npc.cs         # Non-combat NPC
├── room.cs        # Room base class (RoomBase, OutdoorRoomBase, IndoorRoomBase)
├── item.cs        # Item base (ItemBase, ContainerBase)
├── weapon.cs      # Weapon base
└── armor.cs       # Armor base
```

### Command dispatch ✅

- `ICommand` interface — Name, Aliases, Usage, Description, Category, IsWizardOnly, ExecuteAsync()
- `CommandRegistry` — register and lookup commands, categorized help
- `CommandContext` — execution context with state, player, output access
- `CommandFactory` — creates registry with all standard commands

### Social commands ✅

- `shout <message>` / `yell` — speak to adjacent rooms
- `whisper <player> <message>` / `tell` / `msg` — private message
- `who` / `players` / `online` — list online players
- Pre-defined emotes: `bow`, `wave`, `laugh`, `smile`, `nod`, `shake`, `shrug`, `sigh`, `cheer`, `think`, `cry`, `dance`, `yawn`

### Utility commands ✅

- `help [command]` / `?` — show help with categories
- `score` / `sc` — show detailed player stats (HP bar, XP to next level, equipment stats)
- `time` / `date` — show server time and playtime

### Files created ✅

- `Mud/Commands/ICommand.cs` — command interface
- `Mud/Commands/CommandBase.cs` — abstract base class
- `Mud/Commands/CommandContext.cs` — execution context
- `Mud/Commands/CommandRegistry.cs` — command lookup and help
- `Mud/Commands/CommandFactory.cs` — command registration
- `Mud/Commands/Social/ShoutCommand.cs` — shout to adjacent rooms
- `Mud/Commands/Social/WhisperCommand.cs` — private messages
- `Mud/Commands/Social/WhoCommand.cs` — list online players
- `Mud/Commands/Social/EmoteCommands.cs` — 13 predefined emotes
- `Mud/Commands/Utility/HelpCommand.cs` — categorized help system
- `Mud/Commands/Utility/TimeCommand.cs` — time and playtime
- `Mud/Commands/Utility/ScoreCommand.cs` — detailed player stats
- `World/std/room.cs` — RoomBase, OutdoorRoomBase, IndoorRoomBase, DarkRoomBase

### Files modified ✅

- `Mud/Network/ISession.cs` — added IsWizard property
- `Mud/Network/ConsoleSession.cs` — implemented IsWizard (default true)
- `Mud/Network/TelnetSession.cs` — implemented IsWizard
- `Mud/CommandLoop.cs` — integrated CommandRegistry for extensible commands

### Acceptance criteria ✅

- [x] Full World/std/ library
- [x] Command registry with help
- [x] Social commands work
- [x] Score shows all player stats

---

## Phase 15 — Configuration ✅ COMPLETE

**Goal**: Centralized configuration via appsettings.json and cross-platform support.

### Implementation ✅

- `appsettings.json` — JSON configuration file in project root ✅
- `DriverSettings` — C# configuration class with strongly-typed sections ✅
- Microsoft.Extensions.Configuration packages for binding ✅
- Command-line argument overrides (`--port`) ✅

### Configuration sections ✅

| Section | Settings |
|---------|----------|
| `Server` | Port, MaxConnections, WelcomeMessage, MudName, Version |
| `Paths` | WorldDirectory, SaveDirectory, SaveFileName, StartRoom, PlayerBlueprint |
| `GameLoop` | LoopDelayMs, DefaultHeartbeatSeconds, AutoSaveEnabled, AutoSaveIntervalMinutes |
| `Combat` | RoundIntervalSeconds, FleeChancePercent, UnarmedMinDamage, UnarmedMaxDamage |
| `Security` | HookTimeoutMs, CallOutTimeoutMs, HeartbeatTimeoutMs, VerboseLogging |
| `Player` | StartingHP, CarryCapacity, RegenPerHeartbeat, XpMultiplier, BaseXpPerLevel |

### Cross-platform support ✅

- Path normalization for Windows/Linux/macOS ✅
- Forward slashes in csproj globs ✅
- Platform-agnostic file operations ✅

### Files created ✅

| File | Purpose |
|------|---------|
| `appsettings.json` | Configuration file |
| `Mud/Configuration/DriverSettings.cs` | Strongly-typed settings classes |

### Files modified ✅

| File | Change |
|------|--------|
| `JitRealm.csproj` | Added configuration packages, cross-platform globs |
| `Program.cs` | Load and bind settings |
| `Mud/Network/GameServer.cs` | Use settings for port, welcome message, etc. |
| `Mud/CommandLoop.cs` | Use settings for version, start room |
| `Mud/ObjectManager.cs` | Platform-specific path normalization |

### Acceptance criteria ✅

- [x] appsettings.json loaded at startup
- [x] Settings bound to DriverSettings class
- [x] Command-line args override config file
- [x] Works on Windows, Linux, macOS

---

## Post v0.15 Polish ✅ COMPLETE

**Goal**: Quality-of-life improvements for players and world builders.

### Item Aliases ✅

- `IItem.Aliases` property — list of alternative names
- `FindItem()` in MudContext searches by name or any alias
- All example items updated with aliases:
  - rusty_sword: "sword", "rusty sword", "blade", "weapon"
  - health_potion: "potion", "red potion", "health potion", "vial"
  - leather_vest: "vest", "leather vest", "leather armor", "armor"
  - iron_helm: "helm", "helmet", "iron helm", "iron helmet"

### Object Details ✅

- `IMudObject.Details` property — `IReadOnlyDictionary<string, string>`
- Maps keywords to detailed descriptions for "look at X" commands
- Default empty dictionary in MudObjectBase
- `LookAtDetailAsync()` in GameServer searches:
  1. Room details
  2. Inventory items
  3. Room items
  4. NPCs/other objects
- Example rooms updated with details:
  - start.cs: walls, stone, cursor, terminal, ground, floor, symbols
  - meadow.cs: grass, sky, clouds, flowers, wildflowers, breeze

### Command Shortcuts ✅

- `l` as alias for `look`
- Direction shortcuts: `n`, `s`, `e`, `w`, `u`, `d`
- Full direction names work without `go` prefix: `north`, `south`, etc.
- Updated help text to show all shortcuts

### Files modified

| File | Change |
|------|--------|
| `Mud/IMudObject.cs` | Added `Details` property |
| `Mud/MudObjectBase.cs` | Added default empty `Details` |
| `Mud/IItem.cs` | Added `Aliases` property |
| `World/std/item.cs` | Added `Aliases` implementation |
| `Mud/MudContext.cs` | Updated `FindItem()` to check aliases |
| `Mud/Network/GameServer.cs` | Added `LookAtDetailAsync()`, direction shortcuts |
| `World/Rooms/start.cs` | Added room details |
| `World/Rooms/meadow.cs` | Added room details |
| `World/Items/*.cs` | Added aliases to all items |

---

## Phase 16 — Player Accounts ✅ COMPLETE

**Goal**: Persistent player accounts with login/registration system.

### Login Flow ✅

On connect, players choose:
1. **(L)ogin** — authenticate with existing account
2. **(C)reate** — create new player account

### Account Creation ✅

- Name validation: 3-20 characters, alphanumeric, starts with letter
- Password: minimum 4 characters
- Password confirmation required
- SHA256 hash with random 16-byte salt

### Player File Structure ✅

```
players/
├── m/
│   └── merlin.json
├── b/
│   └── bob.json
└── ...
```

### Player JSON Format ✅

```json
{
  "version": 1,
  "name": "Merlin",
  "passwordHash": "base64_sha256_hash",
  "passwordSalt": "base64_random_salt",
  "createdAt": "2025-12-30T15:00:00Z",
  "lastLogin": "2025-12-30T15:30:00Z",
  "isWizard": false,
  "state": {
    "level": 5,
    "experience": 1200,
    "hp": 100,
    "max_hp": 100
  },
  "location": "Rooms/meadow.cs",
  "inventory": ["Items/sword.cs#000001"],
  "equipment": {
    "MainHand": "Items/sword.cs#000001"
  }
}
```

### Persistence Features ✅

- **State**: All IStateStore variables (HP, XP, Level, playtime)
- **Location**: Saved room ID, falls back to start room if invalid
- **Inventory**: Items saved by blueprint ID, re-cloned on login
- **Equipment**: Slots and items restored after inventory

### Security ✅

- Duplicate login prevention (can't login if already online)
- Case-insensitive player names
- Password not stored (only salted hash)

### Files created ✅

| File | Purpose |
|------|---------|
| `Mud/Players/PlayerAccountData.cs` | Serialization format for player files |
| `Mud/Players/PlayerAccountService.cs` | Account management, password hashing |

### Files modified ✅

| File | Change |
|------|--------|
| `Mud/Configuration/DriverSettings.cs` | Added `PlayersDirectory` to PathSettings |
| `appsettings.json` | Added `PlayersDirectory: "players"` |
| `Mud/Network/GameServer.cs` | Login/create flow, save on logout |

### Acceptance criteria ✅

- [x] New players can create accounts
- [x] Existing players can login
- [x] Invalid credentials rejected
- [x] Duplicate logins prevented
- [x] Player state persists across sessions
- [x] Inventory and equipment restored
- [x] Location restored (with fallback)

---

## Post v0.16 Polish ✅ COMPLETE

**Goal**: Quality-of-life improvements for gameplay and wizard tools.

### Item Grouping & Formatting ✅

- New `ItemFormatter` utility class in `Mud/ItemFormatter.cs`
- Groups duplicate items in displays: "2 rusty swords" instead of "rusty sword, rusty sword"
- Adds proper articles: "a rusty sword", "an iron helmet"
- English pluralization with irregular word support (knife→knives, elf→elves, dwarf→dwarves, etc.)
- Applied to room contents ("You see: ...") and inventory display

### "here" Keyword for Wizard Commands ✅

- Wizards can reference current room as "here" in commands
- New `ResolveObjectId()` helper in `CommandContext`
- Supported commands: `reload here`, `stat here`, `patch here`, `reset here`, `destruct here`, `unload here`

### Time Command Fix ✅

- `time`/`date` command now works (was missing from GameServer dispatch)
- Shows server time, session duration, total playtime

### Files added

| File | Purpose |
|------|---------|
| `Mud/ItemFormatter.cs` | Item grouping, pluralization, articles |

### Files modified

| File | Change |
|------|--------|
| `Mud/Commands/CommandContext.cs` | Added `ResolveObjectId()` for "here" keyword |
| `Mud/Commands/Wizard/WizardCommands.cs` | Updated 6 commands to use `ResolveObjectId()` |
| `Mud/Network/GameServer.cs` | Item grouping in room/inventory, time command handler |

---

## Phase 17 — LLM-Powered NPCs ✅ COMPLETE

**Goal**: NPCs that react intelligently to the world using LLM (Large Language Model) integration.

### LLM Service ✅

- `ILlmService` interface — abstraction for LLM providers
- `OpenAILlmService` — OpenAI/compatible API implementation
- `LlmSettings` — configuration (endpoint, model, API key, enabled flag)
- Async completion with system prompt and user message

### NPC Capabilities System ✅

- `NpcCapabilities` flags enum — species-based action limitations:
  - `CanSpeak`, `CanEmote`, `CanAttack`, `CanFlee`
  - `CanManipulateItems`, `CanTrade`, `CanFollow`, `CanWander`, `CanUseDoors`
  - Presets: `Animal`, `Humanoid`, `Beast`, `Merchant`

### Room Event System ✅

- `RoomEvent` class — observable events in rooms
- `RoomEventType` enum — Speech, Emote, Arrival, Departure, Combat, ItemTaken, ItemDropped, Death
- NPCs observe all room activity via `OnRoomEventAsync(RoomEvent, IMudContext)`
- Events include actor, message, direction, target information

### NPC Context Building ✅

- `NpcContext` class — complete environmental awareness for NPCs
- Includes: room info, exits, players, other NPCs, items, combat state, recent events
- `BuildEnvironmentDescription()` — human-readable context for LLM
- `BuildActionInstructions()` — capability-aware action suggestions

### NPC Command Execution ✅

- `NpcCommandExecutor` — allows NPCs to issue player-like commands
- Supported commands: say, emote, go, get, drop, kill, flee
- Direction shortcuts: n/s/e/w/u/d
- Respects `NpcCapabilities` (cat can't speak, goblin can)
- `ExecuteCommandAsync(command)` added to IMudContext

### Generic Emote Command ✅

- `emote <action>` / `me <action>` — custom emotes for players
- Example: "emote looks around" → "Alice looks around"

### Example LLM NPCs ✅

- `World/npcs/cat.cs` — animal NPC (emotes only, no speech)
- `World/npcs/goblin.cs` — humanoid monster (full capabilities)
- Both react to room events with LLM-generated responses

### Files created

| File | Purpose |
|------|---------|
| `Mud/AI/ILlmService.cs` | LLM service interface |
| `Mud/AI/OpenAILlmService.cs` | OpenAI API implementation |
| `Mud/AI/ILlmNpc.cs` | LLM NPC interface, RoomEvent, NpcCapabilities |
| `Mud/AI/NpcContext.cs` | Environmental context for NPCs |
| `Mud/AI/NpcCommandExecutor.cs` | NPC command execution |
| `Mud/Network/NpcSession.cs` | Session implementation for NPCs |
| `World/npcs/cat.cs` | Example animal NPC |

### Files modified

| File | Change |
|------|--------|
| `Mud/IMudContext.cs` | Added LLM methods, ExecuteCommandAsync, CurrentObjectId |
| `Mud/MudContext.cs` | Implemented LLM methods and command execution |
| `Mud/WorldState.cs` | Added LlmService, NpcCommands, RoomEventLog |
| `Mud/Network/GameServer.cs` | Room events, emote command, NPC event triggering |
| `Mud/Configuration/DriverSettings.cs` | Added LlmSettings section |
| `appsettings.json` | Added LLM configuration |

### Acceptance criteria ✅

- [x] LLM service connects to OpenAI-compatible API
- [x] NPCs react to room events (speech, arrivals, etc.)
- [x] NPC capabilities limit available actions
- [x] Cat can only emote, goblin can speak
- [x] NPCs can execute commands like players
- [x] Environmental context includes room, entities, items

### LivingBase LLM Integration ✅

Refactored LLM NPC support into `LivingBase` for minimal boilerplate:

**Description Property:**
- `ILiving.Description` — shown when players look at NPCs
- Override in NPC classes for custom descriptions
- Default: `$"You see {Name}."` (generic fallback)

**Event Processing (in LivingBase):**
- `QueueLlmEvent(event, ctx)` — queue events for heartbeat processing
- `ProcessPendingLlmEvent(ctx)` — auto-called in Heartbeat for ILlmNpc
- `HasPendingLlmEvent` — check for pending events
- `GetLlmReactionInstructions(event)` — context-aware reaction instructions:
  - **Speech events**: instructs LLM to respond with speech ("You MUST reply with speech")
  - **Other events**: instructs LLM to use emotes

**System Prompt Builder (in LivingBase):**
- `BuildSystemPrompt()` — generates consistent prompts from properties
- `NpcIdentity` — who they are (defaults to Name)
- `NpcNature` — physical description
- `NpcCommunicationStyle` — speech patterns
- `NpcPersonality` — character traits
- `NpcExamples` — example responses
- `NpcExtraRules` — character-specific rules

**Auto-generated prompt rules:**
- Emote format with asterisks (third-person: `*smiles*` not `*I smile*`)
- Speech format with quotes (`"Hello!"`)
- "NEVER use first person (I, me, my)"
- "You CANNOT speak" (if !CanSpeak)
- "NEVER break character"
- "Respond with exactly ONE action per event"

**Response Parsing:**
- `NpcCommandExecutor` parses both `*emote*` and `[emote]` patterns
- Only executes first action per response (spam prevention)
- Truncates speech to first sentence
- **First-person auto-correction**: "I smile" → "smiles", "I look around" → "looks around"

**Persistent NPC Memory + Goals (Postgres + pgvector) ✅**
- Optional driver-owned memory system stored in `WorldState.MemorySystem`
- Enabled via `appsettings.json` → `Memory.Enabled=true` and `Memory.ConnectionString`
- Stores **per-NPC goals** + **per-NPC long-term memories** and a **shared world knowledge base**
- NPC prompt context now includes:
  - `GoalSummary`
  - `LongTermMemories` (top-K)
  - `WorldKnowledge` (top-K)
- Writes are buffered via a bounded in-process queue (DropOldest) to protect the game loop
- Default memory promotion happens on salient room events (combat/death/item-given + directly-addressed speech)

**Wizard Story-Builder Model ✅**
- `appsettings.json` `Llm.StoryModel` can point at a larger creative GGUF (e.g. 29B) for lore/quest/scene generation.
- Wizard command: `story <prompt>` (aliases: `lore`, `write`) uses the story model profile and does not affect NPC latency/cost.

**Autonomous Goal Pursuit + Semantic Memory Recall ✅**
- NPCs with goals will periodically take **one** autonomous step toward an active goal even when the room is quiet (rate-limited).
- Memory recall supports **semantic reranking** when both are enabled:
  - PostgreSQL has `pgvector` installed (extension `vector`)
  - `appsettings.json` has `Llm.EmbeddingModel` set (Ollama `/api/embed`), and `Memory.UsePgvector` is enabled
- When semantic recall is available, NPC context building embeds a query derived from the current event/goal and uses pgvector distance (`<=>`) to select relevant memories.
- Survival is implemented as a **need/drive** (`npc_needs`), not a goal (`npc_goals`), and is always the top drive for all living entities.

**Stackable Goals with Priority ✅**
- Goals are now stackable (multiple goals per NPC) with importance-based priority
- Lower importance = higher priority. Importance levels:
  - **Drive: survive** — highest priority for all living entities (always-on; not stored as a goal)
  - `GoalImportance.Combat` (5) — active combat situations
  - `GoalImportance.Urgent` (10) — urgent tasks
  - `GoalImportance.Default` (50) — normal priority (default for LLM-set goals)
  - `GoalImportance.Background` (100) — low priority background tasks
- Three ways to set goals:
  1. **Source code** via `IHasDefaultGoal` interface (includes `DefaultGoalImportance`)
  2. **LLM markup** via `[goal:type]`, `[goal:clear]`, `[goal:done type]`
  3. **Wizard command** `goal <npc> [type [importance] [target]]`
- Database schema: composite primary key `(npc_id, goal_type)` with `importance` column
- IMudContext methods: `SetGoalAsync`, `ClearGoalAsync`, `ClearAllGoalsAsync`, `GetGoalAsync`, `GetAllGoalsAsync`

**Goal Plans (Step-by-Step Tasks) ✅**
- Goals can have plans — ordered lists of steps NPCs work through
- Plans stored in goal's `params` JSONB field as `{plan: {steps: [...], currentStep: N, completedSteps: [...]}}`
- NPCs are instructed about plan markup in their system prompt (via `LivingBase.BuildSystemPrompt()`)
- LLM markup:
  - `[plan:step1|step2|step3]` — set plan for highest priority goal (pipe-separated)
  - `[step:done]` / `[step:complete]` — complete current step, advance to next
  - `[step:skip]` / `[step:next]` — skip current step without completing
- Wizard commands:
  - `goal <npc> plan <type> <step1|step2|...>` — set plan
  - `goal <npc> plan <type> clear` — clear plan
- LLM context shows: `[50] sell_items (step 2/4: "show_items")`
- Auto-completion: when all steps complete, goal is cleared and default restored if applicable
- Key files: `Mud/AI/GoalPlan.cs`, `Mud/AI/NpcCommandExecutor.cs`, `World/std/living.cs`

**NPC Engagement System:**
Smart speech detection to reduce spam and make NPCs feel more natural:
- **1:1 conversation** — If only NPC and player in room, all speech is directed
- **Direct address** — Speech containing NPC's name or alias is directed (e.g., "hey shopkeeper")
- **Engagement tracking** — When NPC responds, they stay "engaged" with that player
  - Engaged players get immediate responses without addressing by name
  - Engagement expires after `EngagementTimeoutSeconds` (default 60s)
  - Engagement clears when player leaves room
- **Ambient chatter** — Unaddressed speech in crowded rooms queued for rare heartbeat reaction
- Helper methods: `IsEngagedWith()`, `EngageWith()`, `DisengageFrom()`, `IsAloneWithSpeaker()`, `IsSpeechDirectlyAddressed()`

### Files modified (LivingBase refactor)

| File | Change |
|------|--------|
| `Mud/ILiving.cs` | Added `Description` property to interface |
| `World/std/living.cs` | Added Description, LLM event queue, prompt builder, context-aware reactions |
| `World/npcs/cat.cs` | Added explicit Description, uses base class features |
| `World/npcs/goblin.cs` | Added explicit Description, uses base class features |
| `World/npcs/shopkeeper.cs` | Added explicit Description, uses base class features |
| `Mud/AI/NpcCommandExecutor.cs` | Bracket pattern, one-action limit, first-person emote fix |
| `Mud/CommandLoop.cs` | Updated look command to show living.Description |
| `Mud/Network/GameServer.cs` | Updated look command to show living.Description |
| `Mud/AI/NpcMemorySystem.cs` | Postgres-backed memory/goals system with bounded write queue |
| `Mud/AI/PostgresMemorySchema.cs` | Idempotent schema/extension initialization |
| `Mud/AI/PostgresNpcMemoryStore.cs` | Postgres memory store (pgvector-aware) |
| `Mud/AI/PostgresNpcGoalStore.cs` | Postgres goal store (stackable goals with importance) |
| `Mud/Commands/Wizard/GoalCommand.cs` | Wizard command for viewing/setting NPC goals |
| `Mud/Commands/Wizard/StoryCommand.cs` | Wizard story/lore generator using the configured StoryModel |
| `Mud/AI/PostgresWorldKnowledgeBase.cs` | Postgres world KB store |
| `Mud/AI/MemoryPromotionRules.cs` | Conservative memory promotion rules |
| `Mud/MudContext.cs` | BuildNpcContext is async and retrieves goal/memory/KB for prompts |
| `Mud/IMudContext.cs` | `BuildNpcContextAsync(ILiving, focalPlayerName)` |
| `Mud/Commands/CommandContext.cs` | Promotes salient room events into per-NPC memory (queued) |
| `Mud/Configuration/MemorySettings.cs` | Memory settings block |

---

## Phase 18 — Web Frontend

**Goal**: Modern web-based client with wizard tools for world building.

### Architecture

Related design docs:
- `docs/NPC_MEMORY_AND_GOALS_PLAN.md` — Per-NPC goals + long-term memory (Postgres + pgvector), shared world KB

```
┌─────────────────────┐         ┌─────────────────────────────────────┐
│   SvelteKit App     │◄──WS───►│  JitRealm C# Server                 │
│   (TypeScript)      │         │                                     │
├─────────────────────┤         ├─────────────────────────────────────┤
│ - Game Terminal     │         │ - WebSocket Server (port 8080)      │
│ - Stats Panel       │         │ - JSON Protocol Handler             │
│ - Wizard Editor*    │         │ - File API (wizard only)            │
│ - File Explorer*    │         │ - Existing: Telnet, Game Loop       │
└─────────────────────┘         └─────────────────────────────────────┘
                                 * = wizard-only features
```

### Phase 18a — Backend WebSocket API

**Add IsWizard to player system:**
- `Mud/IPlayer.cs` — Add `bool IsWizard { get; }`
- `World/std/player.cs` — Implement from state store
- New command: `wizard <playername>` (admin only)

**WebSocket server infrastructure:**
```
Mud/Network/
├── WebSocketServer.cs       # Accept WS connections (HttpListener)
├── WebSocketSession.cs      # ISession implementation for WS
├── Protocol/
│   ├── MessageTypes.cs      # Client/Server message type enums
│   ├── ClientMessage.cs     # Incoming: { type, payload }
│   ├── ServerMessage.cs     # Outgoing: { type, payload }
│   └── MessageHandler.cs    # Route messages, check wizard perms
└── FileOperations.cs        # Safe file read/write for wizards
```

**JSON protocol message types:**

| Client → Server | Description | Wizard Only |
|-----------------|-------------|-------------|
| `Auth_Login` | `{ name }` | No |
| `Command` | `{ command }` | No |
| `File_List` | `{ path }` | Yes |
| `File_Read` | `{ path }` | Yes |
| `File_Write` | `{ path, content }` | Yes |
| `Blueprint_Reload` | `{ blueprintId }` | Yes |
| `Object_Stat` | `{ objectId }` | Yes |

| Server → Client | Description |
|-----------------|-------------|
| `Auth_Success` | `{ playerId, playerName, isWizard }` |
| `Auth_Failed` | `{ reason }` |
| `Room_Look` | `{ name, description, exits, contents }` |
| `Message` | `{ type, from, text }` |
| `Combat_Round` | `{ attacker, defender, damage, hp }` |
| `Player_Stats` | `{ hp, maxHp, level, xp }` |
| `File_List_Result` | `{ files[] }` |
| `File_Content` | `{ path, content }` |
| `Error` | `{ code, message }` |

**Security:**
- All wizard endpoints check `session.IsWizard`
- File paths validated (no traversal outside World/)
- WebSocket connections require authentication

### Phase 18b — Game Event Broadcasting

**Push events to WebSocket clients:**
- Room changes → `Room_Look`
- Combat rounds → `Combat_Round`
- HP/stats changes → `Player_Stats`
- Messages → `Message`

**WebGameServer loop:**
```csharp
while (!ct.IsCancellationRequested)
{
    // Process incoming WebSocket messages
    // Process game tick (heartbeats, combat, callouts)
    // Broadcast state updates to clients
    await Task.Delay(100, ct);
}
```

### Phase 18c — SvelteKit Frontend

**Tech stack:**
- SvelteKit 2.x + Svelte 5
- xterm.js — terminal emulation
- Monaco Editor — code editing
- svelte-splitpanes — resizable panels
- bits-ui — UI components

**Project structure:**
```
web/
├── src/
│   ├── lib/
│   │   ├── stores/          # auth, game, connection
│   │   ├── components/
│   │   │   ├── Terminal.svelte
│   │   │   ├── StatsPanel.svelte
│   │   │   └── wizard/      # FileExplorer, CodeEditor
│   │   └── protocol/        # WebSocket client, types
│   └── routes/
│       ├── +page.svelte     # Login
│       └── game/+page.svelte # Main interface
└── package.json
```

### Phase 18d — Player UI (Everyone)

**Components:**
- Terminal — xterm.js for game output with ANSI colors
- CommandInput — text input with history
- StatsPanel — HP bar, level, XP

**Layout:**
```
┌─────────────────────────────────────────┐
│ Game Terminal        │  Stats Panel     │
│ ──────────────────── │  ─────────────── │
│ > look               │  HP: ████░ 80/100│
│ A sunny meadow...    │  Level: 5        │
│ >                    │  XP: 4500        │
└─────────────────────────────────────────┘
```

### Phase 18e — Wizard UI (Wizard Only)

**Additional tabs/panels for wizards:**
- World Editor tab — file explorer + Monaco editor
- Objects tab — loaded blueprints/instances inspector

**Layout (wizard view):**
```
┌─────────────────────────────────────────────────────────┐
│  [Game] [World Editor] [Objects]                        │
├────────────────┬────────────────────────────────────────┤
│ File Explorer  │  Monaco Editor                         │
│ 📁 World/      │  [Save] [Reload]                       │
│   📁 Rooms/    │  ───────────────────────────────────── │
│     meadow.cs  │  public class Meadow : IRoom { }      │
│   📁 npcs/     │                                        │
└────────────────┴────────────────────────────────────────┘
```

**Wizard workflow:**
1. Browse files in File Explorer
2. Click file → loads in Monaco Editor
3. Edit C# code
4. Save → `File_Write` to server
5. Reload → `Blueprint_Reload` to hot-reload

### Files to create

**Backend (C#):**
| File | Purpose |
|------|---------|
| `Mud/Network/WebSocketServer.cs` | Accept WebSocket connections |
| `Mud/Network/WebSocketSession.cs` | ISession for WebSocket |
| `Mud/Network/WebGameServer.cs` | Game loop for WebSocket clients |
| `Mud/Network/Protocol/MessageTypes.cs` | Message type enums |
| `Mud/Network/Protocol/ClientMessage.cs` | Incoming message structure |
| `Mud/Network/Protocol/ServerMessage.cs` | Outgoing message structure |
| `Mud/Network/Protocol/MessageHandler.cs` | Route and handle messages |
| `Mud/Network/Protocol/FileOperations.cs` | Wizard file read/write |

**Files to modify:**
| File | Change |
|------|--------|
| `Mud/IPlayer.cs` | Add `bool IsWizard` |
| `World/std/player.cs` | Implement IsWizard |
| `Program.cs` | Add `--web` flag for WebSocket server |

**Frontend (web/):**
| File | Purpose |
|------|---------|
| `web/src/lib/stores/auth.ts` | Auth state with isWizard |
| `web/src/lib/stores/game.ts` | Game state (room, stats) |
| `web/src/lib/protocol/client.ts` | WebSocket client |
| `web/src/lib/components/Terminal.svelte` | xterm.js wrapper |
| `web/src/lib/components/wizard/FileExplorer.svelte` | File tree |
| `web/src/lib/components/wizard/CodeEditor.svelte` | Monaco wrapper |

### Acceptance criteria

- [ ] WebSocket server accepts connections on port 8080
- [ ] JSON protocol handles auth, commands, file operations
- [ ] Wizard endpoints check session.IsWizard
- [ ] SvelteKit app connects and authenticates
- [ ] Terminal displays game output with colors
- [ ] Stats panel shows HP/Level/XP
- [ ] Wizards see additional tabs
- [ ] File explorer browses World/ directory
- [ ] Monaco editor edits .cs files
- [ ] Save writes file to server
- [ ] Reload hot-reloads blueprint

---

## Implementation Priority

### Core lpMUD Feel (completed)
- Phase 8: Living Foundation ✅
- Phase 9: Player as World Object ✅
- Phase 10: Items & Inventory ✅
- Phase 13: NPCs & AI ✅

### Complete Experience (completed)
- Phase 11: Equipment ✅
- Phase 12: Combat ✅

### Polish & Accessibility
- Phase 14: Mudlib Polish ✅
- Phase 15: Configuration ✅
- Phase 16: Player Accounts ✅
- Phase 17: LLM-Powered NPCs ✅

### Web & Future (next)
- Phase 18: Web Frontend

### Future Enhancements
- Spell/magic system
- Quest system
- Crafting
- Guilds/classes
- Visual room/map editor
- GraphRAG for NPC memory (knowledge graph + vector search)

---

## Daemon System ✅ COMPLETE

**Goal**: Provide long-lived singleton service objects for shared game systems.

### What Are Daemons?

Daemons are inspired by lpMUD's daemon pattern - central service objects that provide shared game systems like time, weather, economy, etc. Unlike regular world objects:

- **Singletons**: One instance per daemon type
- **Long-lived**: Loaded on startup, persist until shutdown
- **Globally accessible**: Any world code can query via `ctx.World.GetDaemon<T>()`
- **Heartbeat-enabled**: Support periodic updates

### Implementation

**New files created:**

| File | Purpose |
|------|---------|
| `Mud/IDaemon.cs` | Interface for daemon service objects + ITimeDaemon, IWeatherDaemon |
| `Mud/DaemonRegistry.cs` | Singleton lookup registry |
| `World/std/daemon.cs` | DaemonBase and ShutdownDaemonBase classes |
| `World/daemons/time_d.cs` | TIME_D - world time simulation (implements ITimeDaemon) |
| `World/daemons/weather_d.cs` | WEATHER_D - weather simulation (implements IWeatherDaemon) |

**Files modified:**

| File | Change |
|------|--------|
| `Mud/WorldState.cs` | Added DaemonRegistry, LoadDaemonsAsync(), ShutdownDaemons() |
| `Mud/Security/ISandboxedWorldAccess.cs` | Added GetDaemon<T>(), GetDaemon(), ListDaemonIds() |
| `Mud/Security/SandboxedWorldAccess.cs` | Implemented daemon query methods |
| `Mud/Room.cs` | Added IsOutdoors, IsLit to IRoom interface |
| `Mud/Commands/Navigation/LookCommand.cs` | Integrated time/weather display for outdoor rooms |
| `Program.cs` | Added daemon loading on startup, shutdown on exit |

### Daemon Interfaces

Driver-defined interfaces for type-safe daemon access:

```csharp
public interface ITimeDaemon : IDaemon
{
    int Hour { get; }
    int Minute { get; }
    string TimeString { get; }
    bool IsNight { get; }
    bool IsDay { get; }
    string PeriodDescription { get; }
}

public interface IWeatherDaemon : IDaemon
{
    bool IsRaining { get; }
    bool IsLowVisibility { get; }
    bool IsDangerous { get; }
    string WeatherDescription { get; }
}
```

### Outdoor Room Integration

Rooms with `IsOutdoors = true` automatically show time and weather in the `look` command:

```
A Worn Path
A dusty path winds between rolling hills...
The sky begins to lighten as dawn approaches. The sky is clear and calm.
Exits: north, south
```

- `OutdoorRoomBase` sets `IsOutdoors = true` by default
- `IndoorRoomBase` sets `IsOutdoors = false`
- No changes needed to individual rooms - integration is automatic

### Built-in Daemons

**TIME_D** - World time daemon:
- Tracks hour, minute, day, month, year
- Time periods: Dawn, Morning, Midday, Afternoon, Evening, Dusk, Night
- Classic LPMud timing: 1 game day = 1 real hour (TimeMultiplier = 24)
- Properties: Hour, Minute, Day, Month, Year, Period, IsDay, IsNight, PeriodDescription

**WEATHER_D** - Weather simulation daemon:
- Weather conditions: Clear, Cloudy, Overcast, LightRain, Rain, HeavyRain, Thunderstorm, Fog, Snow, Blizzard
- Temperature and wind strength tracking
- Gradual weather transitions
- Properties: CurrentWeather, Temperature, WindStrength, IsRaining, IsLowVisibility, IsDangerous

### Creating Custom Daemons

```csharp
// World/daemons/my_daemon.cs
public sealed class MyDaemon : DaemonBase
{
    public override string DaemonId => "MY_D";
    public override TimeSpan HeartbeatInterval => TimeSpan.FromMinutes(1);

    protected override void OnInitialize(IMudContext ctx)
    {
        // Setup initial state
    }

    protected override void OnHeartbeat(IMudContext ctx)
    {
        // Periodic updates
    }
}
```

### Accessing Daemons from World Code

```csharp
// Use interfaces for type-safe access
var timeD = ctx.World.GetDaemon<ITimeDaemon>("TIME_D");
if (timeD?.IsNight == true)
{
    ctx.Tell(playerId, "It's dark outside.");
}

var weatherD = ctx.World.GetDaemon<IWeatherDaemon>("WEATHER_D");
if (weatherD?.IsRaining == true)
{
    ctx.Tell(playerId, "Rain falls steadily.");
}
```

### Acceptance Criteria ✅

- [x] Daemons auto-load from World/daemons/ on startup
- [x] DaemonRegistry provides singleton lookup
- [x] Daemons support heartbeat for periodic updates
- [x] World code can query daemons via ISandboxedWorldAccess
- [x] TIME_D tracks world time with classic LPMud timing
- [x] WEATHER_D simulates weather conditions
- [x] ITimeDaemon and IWeatherDaemon interfaces for type-safe access
- [x] Outdoor rooms automatically display time/weather in `look` command
- [x] IRoom interface extended with IsOutdoors and IsLit properties
