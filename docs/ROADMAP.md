# Roadmap (lpMUD-inspired)

## Phase 1 — Blueprints vs Clones (core lpMUD feel) ✅ COMPLETE

- File-backed object = blueprint: `Rooms/meadow.cs` ✅
- Runtime instance = clone: `Rooms/meadow.cs#000001` ✅
- Commands implemented: ✅
  - `clone <blueprintId>`
  - `destruct <instanceId>`
  - `stat <id>`
  - `blueprints`
- Instance state separate from blueprint code via `IStateStore` ✅

## Phase 2 — Driver hooks ✅ COMPLETE

- `IOnLoad(IMudContext ctx)` — wired in ObjectManager ✅
- `IOnReload(IMudContext ctx, string oldTypeName)` — called during blueprint reload ✅
- `IOnEnter(IMudContext ctx, string whoId)` — triggered when player enters room ✅
- `IOnLeave(IMudContext ctx, string whoId)` — triggered when player leaves room ✅
- `IHeartbeat` — scheduled tick with `HeartbeatScheduler` ✅
- `IResettable.Reset()` — triggered via `reset` command ✅
- Messaging: `Tell`, `Say`, `Emote` via `IMudContext` + `MessageQueue` ✅
- Callouts: `CallOut`, `Every`, `CancelCallOut` via `CallOutScheduler` ✅

## Phase 3 — Persistence ✅ COMPLETE

- `save` command — persist world state to JSON file ✅
- `load` command — restore world from saved state ✅
- Automatic load on startup if save exists ✅
- What gets persisted: ✅
  - Player state (name, location)
  - All loaded instances + state stores
  - Container registry

## Phase 4 — Multi-user networking ✅ COMPLETE

- TCP telnet server on configurable port ✅
- ISession abstraction for connection types ✅
- SessionManager for tracking active sessions ✅
- GameServer multi-player game loop ✅
- Player sees other players in rooms ✅
- `say` command for room chat ✅
- `who` command to list online players ✅

## Phase 5 — Security sandboxing ✅ COMPLETE

- Assembly reference whitelisting via `SafeReferences.cs` ✅
- Namespace/type blocking via `ForbiddenSymbolValidator.cs` ✅
- API surface isolation via `ISandboxedWorldAccess` ✅
- Execution timeouts via `SafeInvoker.cs` ✅
- Blocked: System.IO, System.Net, System.Diagnostics, System.Reflection.Emit ✅
- World code can only use safe APIs through IMudContext ✅

## Phase 6 — Living Foundation ✅ COMPLETE

- `ILiving` interface with HP, MaxHP, TakeDamage, Heal, Die ✅
- `IHasStats` optional interface for Strength/Dex/etc. ✅
- Living hooks: `IOnDamage`, `IOnDeath`, `IOnHeal` ✅
- IMudContext methods: `DealDamage()`, `HealTarget()` ✅
- Standard library: `World/std/living.cs` base class ✅
- HP stored in IStateStore for persistence ✅
- Heartbeat triggers natural regeneration ✅

## Phase 7 — Player as World Object ✅ COMPLETE

- `IPlayer` interface extending `ILiving` ✅
- `World/std/player.cs` player blueprint ✅
- Session tracks `PlayerId` and `PlayerName` instead of `Player` class ✅
- Player location via ContainerRegistry ✅
- Player state (HP, XP, Level) persists in IStateStore ✅
- Login/Logout hooks: `OnLogin()`, `OnLogout()` ✅
- New `score` command to show player stats ✅
- Multiple players have separate instances ✅

## Phase 8 — Items & Inventory ✅ COMPLETE

- `IItem` interface — Weight, Value, ShortDescription, LongDescription ✅
- `ICarryable` interface — OnGet, OnDrop, OnGive hooks ✅
- `IContainer` interface — MaxCapacity, IsOpen, Open, Close ✅
- `IHasInventory` interface — CarryCapacity, CarriedWeight, CanCarry ✅
- `IPlayer` now extends `IHasInventory` ✅
- IMudContext methods: Move, GetContainerWeight, GetInventory, FindItem ✅
- Commands: get/take, drop, inventory/inv/i, examine/exam/x ✅
- Standard library: `World/std/item.cs` (ItemBase, ContainerBase) ✅
- Example items: rusty_sword.cs, health_potion.cs ✅
- Weight limit enforced ✅
- Items persist in inventories ✅

## Phase 9 — Equipment System ✅ COMPLETE

- `IEquippable` interface — Slot, OnEquip, OnUnequip ✅
- `IWeapon` interface — MinDamage, MaxDamage, WeaponType ✅
- `IArmor` interface — ArmorClass, ArmorType ✅
- `IHasEquipment` interface — TotalArmorClass, WeaponDamage ✅
- `EquipmentRegistry` — tracks equipped items per slot ✅
- `IPlayer` now extends `IHasEquipment` ✅
- Commands: equip/wield/wear, unequip/remove, equipment/eq ✅
- Standard library: `World/std/weapon.cs`, `World/std/armor.cs` ✅
- Example equipment: rusty_sword (weapon), leather_vest (armor), iron_helm (helmet) ✅
- Equipment persists across save/load ✅
- Automatic stat calculation from equipped items ✅

## Phase 10 — Combat System ✅ COMPLETE

- `ICombatant` interface — Attack, InCombat, CombatTarget, StopCombat, TryFlee ✅
- Combat hooks: `IOnAttack`, `IOnDefend`, `IOnKill` ✅
- `CombatScheduler` — tracks active combats, processes rounds ✅
- Combat rounds: 3-second interval, automatic damage calculation ✅
- Damage = weapon damage + OnAttack - armor - OnDefend ✅
- Commands: kill/attack, flee/retreat, consider/con ✅
- Experience awarded on kill (based on victim MaxHP) ✅
- Combat ends on death or flee ✅
- Flee: 50% success chance, moves to random exit ✅
- Example NPC: goblin.cs for combat testing ✅

## Phase 11 — NPCs & AI ✅ COMPLETE

- `ISpawner` interface — rooms define NPCs to spawn ✅
- `MonsterBase` standard library class — aggressive NPCs ✅
  - ExperienceValue, IsAggressive, AggroDelaySeconds ✅
  - Auto-attack players on sight via OnEnter + CallOut ✅
  - Respawn after death via CallOut ✅
  - Wander behavior via Heartbeat ✅
- `NPCBase` standard library class — friendly NPCs ✅
  - High HP, fast regen, greetings via GetGreeting() ✅
  - NPCs don't die (full heal on damage) ✅
- `ProcessSpawnsAsync()` — tracks and replenishes NPCs in rooms ✅
- Example NPCs: goblin.cs (monster), shopkeeper.cs (friendly) ✅
- Example rooms: meadow.cs (spawns goblin), shop.cs (spawns shopkeeper) ✅

## Phase 14 — Mudlib Polish ✅ COMPLETE

- Command registry system with categorized help ✅
  - `ICommand` interface — Name, Aliases, Usage, Description, Category, IsWizardOnly ✅
  - `CommandRegistry` — lookup, categorization, help generation ✅
  - `CommandContext` — execution context with state access ✅
  - `CommandFactory` — registers all standard commands ✅
- Social commands ✅
  - `shout`/`yell` — speak to adjacent rooms ✅
  - `whisper`/`tell`/`msg` — private messages ✅
  - `who`/`players`/`online` — list online players ✅
  - 13 predefined emotes: bow, wave, laugh, smile, nod, shake, shrug, sigh, cheer, think, cry, dance, yawn ✅
- Utility commands ✅
  - `help [cmd]`/`?` — categorized command help ✅
  - `score`/`sc` — detailed player stats with HP bar ✅
  - `time`/`date` — server time and playtime ✅
- Standard library additions ✅
  - `World/std/room.cs` — RoomBase, OutdoorRoomBase, IndoorRoomBase, DarkRoomBase ✅
- Session wizard support: `ISession.IsWizard` property ✅

## Phase 15 — Configuration ✅ COMPLETE

- `appsettings.json` configuration file ✅
- `DriverSettings` configuration class with sections: ✅
  - `Server` — Port, MaxConnections, WelcomeMessage, MudName, Version
  - `Paths` — WorldDirectory, SaveDirectory, SaveFileName, StartRoom, PlayerBlueprint
  - `GameLoop` — LoopDelayMs, DefaultHeartbeatSeconds, AutoSaveEnabled, AutoSaveIntervalMinutes
  - `Combat` — RoundIntervalSeconds, FleeChancePercent, UnarmedMinDamage, UnarmedMaxDamage
  - `Security` — HookTimeoutMs, CallOutTimeoutMs, HeartbeatTimeoutMs, VerboseLogging
  - `Player` — StartingHP, CarryCapacity, RegenPerHeartbeat, XpMultiplier, BaseXpPerLevel
- Command-line argument overrides (`--port`) ✅
- Cross-platform support (Windows, Linux, macOS) ✅
- Platform-specific path normalization ✅

## Recent Additions (Post v0.15) ✅ COMPLETE

- **Item Aliases** ✅
  - `IItem.Aliases` property for alternative names (e.g., "sword", "blade", "weapon")
  - `FindItem()` searches by name or any alias
  - All example items updated with aliases

- **Object Details** ✅
  - `IMudObject.Details` dictionary for granular descriptions
  - `look at <detail>` command to examine room/object details
  - Example: `look at grass`, `look at walls`, `look at cursor`
  - Details added to start.cs and meadow.cs rooms

- **Command Shortcuts** ✅
  - `l` as shortcut for `look`
  - Direction shortcuts: `n`, `s`, `e`, `w`, `u`, `d`
  - `north`, `south`, `east`, `west`, `up`, `down` as direct commands (no `go` prefix needed)
  - `q` as shortcut for `quit`
  - `?` as shortcut for `help`
  - `sc` as alias for `score`
  - Updated help text to show shortcuts

- **Wizard Commands in Help** ✅
  - All wizard commands registered via `CommandRegistry` with `IsWizardOnly = true`
  - Commands: `blueprints`, `objects`, `clone`, `destruct`, `stat`, `reset`, `reload`, `unload`, `patch`, `save`, `load`
  - Wizard commands appear in `help` output only for wizard users
  - Non-wizards see "Unknown command" for wizard-only commands
  - `patch` command allows viewing/modifying object state variables at runtime

## Phase 16 — Player Accounts ✅ COMPLETE

- **Login/Registration Flow** ✅
  - On connect: prompt for (L)ogin or (C)reate new player
  - Create: name validation (3-20 chars, alphanumeric), password confirmation
  - Login: credential validation, duplicate login prevention

- **Player File Storage** ✅
  - Files stored at `players/{first_letter}/{name}.json`
  - SHA256 password hashing with random 16-byte salt
  - JSON format with version field for migration support

- **Persistent Player Data** ✅
  - State variables (HP, XP, Level, playtime) saved from IStateStore
  - Location saved and restored (falls back to start room if invalid)
  - Inventory saved by item blueprint ID, re-cloned on login
  - Equipment saved by slot, restored after inventory

- **Configuration** ✅
  - `Paths.PlayersDirectory` in appsettings.json (default: "players")
  - Player data auto-saved on quit/logout

- **New Files** ✅
  - `Mud/Players/PlayerAccountData.cs` — serialization format
  - `Mud/Players/PlayerAccountService.cs` — account management service
