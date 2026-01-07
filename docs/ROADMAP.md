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
- `LivingBase` wandering system ✅
  - `WanderChance` property (0-100) — percentage chance per heartbeat
  - `HeartbeatInterval` property — configurable tick rate (default 1 second)
  - Smart direction selection — 80% chance to avoid returning the way they came
  - Wandering skipped if NPC has pending LLM event (reacts first)
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

## Post v0.16 Polish ✅

- **Item Grouping & Formatting** ✅
  - `ItemFormatter` utility class for grouped item display
  - Room contents: "You see: 2 rusty swords" instead of "rusty sword, rusty sword"
  - Inventory: Groups items with combined weights (e.g., "2 rusty swords (10 lbs)")
  - Proper articles: "a rusty sword", "an iron helmet"
  - English pluralization with irregular word support (knife→knives, elf→elves, etc.)

- **"here" Keyword for Wizard Commands** ✅
  - Wizards can reference current room as "here" in commands
  - Supported: `reload here`, `stat here`, `patch here`, `reset here`, `destruct here`, `unload here`
  - `ResolveObjectId()` helper in CommandContext

- **Time Command Fix** ✅
  - `time`/`date` command now works (was missing from GameServer dispatch)
  - Shows server time, session duration, total playtime

## LLM-Powered NPCs ✅

- **LLM Service Integration** ✅
  - `ILlmService` interface — abstraction for LLM providers
  - `OpenAILlmService` — OpenAI-compatible API implementation
  - `LlmSettings` configuration — endpoint, model, API key, enabled flag

- **NPC Capabilities System** ✅
  - `NpcCapabilities` flags enum — species-based action limitations
  - Presets: Animal (emote only), Humanoid (full), Beast, Merchant
  - Capabilities: CanSpeak, CanEmote, CanAttack, CanFlee, CanManipulateItems, etc.

- **Room Event System** ✅
  - `RoomEvent` class — observable room activity
  - Event types: Speech, Emote, Arrival, Departure, Combat, ItemTaken, ItemDropped, Death
  - NPCs observe events via `OnRoomEventAsync(RoomEvent, IMudContext)`

- **LivingBase LLM Integration** ✅
  - `Description` property for NPC look descriptions
  - `Aliases` property for alternative targeting names (e.g., "barnaby", "keeper")
  - `ShortDescription` property for display with article (e.g., "a shopkeeper")
  - `QueueLlmEvent()` / `ProcessPendingLlmEvent()` — heartbeat-based processing
  - `GetLlmReactionInstructions()` — context-aware (speech→speech, other→emote)
  - `BuildSystemPrompt()` — auto-generates consistent NPC prompts
  - Properties: NpcNature, NpcCommunicationStyle, NpcPersonality, NpcExamples, NpcExtraRules

- **NPC Engagement System** ✅
  - Smart speech detection reduces spam and makes NPCs feel natural
  - 1:1 conversations — all speech directed when alone with NPC
  - Direct address — speech containing NPC name/alias is directed
  - Engagement tracking — NPC stays engaged with player for follow-up conversation (60s timeout)
  - Ambient chatter — unaddressed speech queued for rare heartbeat reaction
  - `EngagementTimeoutSeconds` — configurable per-NPC (default 60s)
  - Engagement clears on player departure

- **NPC Command Execution** ✅
  - `NpcCommandExecutor` — NPCs issue player-like commands (say, emote, go, get, drop, kill, flee)
  - First-person emote auto-correction: "I smile" → "smiles"
  - Parses `*emote*` and `[emote]` patterns
  - One action per heartbeat (spam prevention)

- **Example NPCs** ✅
  - `cat.cs` — animal NPC (emotes only, no speech)
  - `goblin.cs` — humanoid monster (full capabilities)
  - `shopkeeper.cs` — merchant NPC (friendly speech)

## Rich Terminal Output (Spectre.Console) ✅

- **Formatting System** ✅
  - `IMudFormatter` interface — abstraction for all output formatting
  - `MudFormatter` — Spectre.Console implementation with ANSI colors
  - `PlainTextFormatter` — fallback for non-ANSI terminals
  - `MudTheme` — consistent color scheme (room names, players, NPCs, combat, etc.)

- **Session Integration** ✅
  - `ISession.SupportsAnsi` — per-session color toggle
  - `ISession.Formatter` — automatic formatter selection based on ANSI support
  - Session-level color persistence

- **Formatted Output** ✅
  - Room display: colored room names, descriptions, exits
  - Player stats: HP bar with gradient colors (green→yellow→red)
  - Inventory/Equipment: formatted tables with weight info
  - Combat messages: colored damage dealt/received

- **Configuration** ✅
  - `Display.DefaultColorsEnabled` in appsettings.json
  - `colors on|off` command for runtime toggle

## Readable Objects & Shop System ✅

- **IReadable Interface** ✅
  - `IReadable` interface — ReadableText, ReadableLabel properties
  - `read <object>` command — reads signs, books, scrolls in room or inventory
  - Checks room contents, room Details, and player inventory

- **SignBase Standard Library** ✅
  - `World/std/sign.cs` — base class for signs and readable fixtures
  - `SignBase` — abstract base with Name, ReadableLabel, ReadableText, Aliases
  - `SimpleSign` — quick static text signs

- **Shop Storage System** ✅
  - `shop_storage.cs` — hidden storage room (no exits)
  - Uses `ISpawner` to stock items (health_potion x3, rusty_sword x2, etc.)
  - Items are actual clones, not hardcoded lists

- **Dynamic Shop Sign** ✅
  - `shop_sign.cs` — reads from storage room, lists items with prices
  - Price calculation: item.Value × 1.5 markup, rounded to nearest 5
  - Groups identical items with counts (e.g., "3x a health potion")
  - Shows "(Out of stock)" when storage is empty

- **Shopkeeper Updates** ✅
  - Removed `IShopkeeper` interface (replaced by storage room)
  - Shopkeeper reads stock from storage room for LLM context
  - Dynamic price calculation matches sign display

## Local Commands & Economy System ✅

- **Local Commands Framework** ✅
  - `IHasCommands` interface — rooms/items can provide context-sensitive commands
  - `LocalCommandInfo` record — Name, Aliases, Usage, Description
  - `LocalCommandDispatcher` — finds and executes local commands
  - Dispatch order: room → inventory items → equipped items
  - Commands appear in `help` output when available (grouped by source)

- **Coin System** ✅
  - `ICoin` interface — Material (Gold/Silver/Copper), Amount (stackable)
  - `CoinMaterial` enum with exchange rates: 1 GC = 100 SC = 10000 CC
  - `CoinHelper` utility — parsing, formatting, add/deduct/transfer coins
  - `coin.cs` blueprint — single blueprint with dynamic name/weight based on state
  - Auto-merge: coins of same material merge when placed in same container
  - `exchange` command — convert between denominations
  - Starting coins: 10 GC + 50 SC for new players
  - Wealth displayed in `score` as breakdown: "75 GC, 50 SC, 0 CC"
  - Coin weight: 0.01 per coin (1000 coins = 10 weight)

- **Shop Buy/Sell Commands** ✅
  - `buy <item>` — purchase from storage room (price = Value × 1.5, rounded to 5 SC)
  - `sell <item>` — sell from inventory (price = Value / 2)
  - Prices displayed in coin breakdown format (e.g., "1 GC, 50 SC")
  - Coins can't be sold (use `exchange` to convert)
  - Weight limit enforced on purchase
  - Items move between player inventory and storage room

- **Linked Rooms** ✅
  - `IHasLinkedRooms` interface — rooms declare related rooms to auto-load
  - Linked rooms have spawns processed when main room becomes active
  - Works for both login (starting in room) and movement (entering room)
  - Example: Shop declares `shop_storage.cs` as linked room

## Village Expansion (Millbrook) ✅

- **New Rooms** ✅
  - `village_square.cs` — Central hub connecting all village locations
  - `tavern.cs` — The Sleepy Dragon Inn with buy/sell food/drinks
  - `tavern_storage.cs` — Hidden cellar storing tavern inventory
  - `post_office.cs` — Millbrook Post Office
  - `blacksmith.cs` — Millbrook Smithy with buy/sell weapons/armor
  - `blacksmith_storage.cs` — Hidden stockroom for smithy inventory
  - `start.cs` — Renamed to "A Worn Path", connects to village square

- **New NPCs** ✅
  - `innkeeper.cs` — Bertram Stoutbarrel, jovial tavern owner
  - `postmaster.cs` — Cornelius Inksworth, fussy bureaucrat
  - `blacksmith.cs` — Greta Ironhand, gruff craftsman

- **New Items** ✅
  - Consumables: `ale_mug.cs` (5 HP), `bread_loaf.cs` (8 HP), `meat_pie.cs` (15 HP)
  - Weapons: `iron_sword.cs` (4-10 dmg), `iron_shield.cs` (4 AC)
  - Signs: `notice_board.cs`, `tavern_menu.cs` (dynamic from storage)

- **Village Map** ✅
  ```
                        [Meadow]
                           |
                        [Start]
                           |
  [Post Office] ←── [Village Square] ──→ [General Store]
                        /     \
               [Blacksmith]  [Tavern]
  ```

## Wizard Tools Expansion ✅

- **Where Command** ✅
  - `where <id|name|alias>` — find object locations in the world
  - Aliases: `locate`, `find`
  - Searches by instance ID, name, or alias
  - Shows object name, full ID, and container location
