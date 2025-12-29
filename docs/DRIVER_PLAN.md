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

## Phase 7 — Security sandboxing (NEXT)

In-process runtime compilation = full trust.
For public use, execute world code out-of-process and expose only a capability-limited API.
