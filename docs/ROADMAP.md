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
