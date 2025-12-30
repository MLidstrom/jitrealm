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
- `score` / `stats` / `status` — show detailed player stats (HP bar, XP to next level, equipment stats)
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

## Phase 16 — Web Frontend

**Goal**: Modern web-based client with wizard tools for world building.

### Architecture

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

### Phase 16a — Backend WebSocket API

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

### Phase 16b — Game Event Broadcasting

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

### Phase 16c — SvelteKit Frontend

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

### Phase 16d — Player UI (Everyone)

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

### Phase 16e — Wizard UI (Wizard Only)

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

### Web & Future (next)
- Phase 16: Web Frontend

### Future Enhancements
- Spell/magic system
- Quest system
- Crafting
- Guilds/classes
- Visual room/map editor
