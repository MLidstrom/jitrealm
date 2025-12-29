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

## Phase 2 — Driver hooks + messaging (NEXT)

### Hook interfaces (optional)

- `IOnLoad` → `OnLoad(IMudContext ctx)` — already wired in ObjectManager
- `IOnEnter` → `OnEnter(IMudContext ctx, string whoId)`
- `IOnLeave` → `OnLeave(IMudContext ctx, string whoId)`
- `IHeartbeat` → `HeartbeatInterval` + `Heartbeat(IMudContext ctx)`
- `IResettable` → `Reset(IMudContext ctx)`

### Messaging primitives

- `Tell(targetId, msg)`
- `Say(roomId, msg)`
- `Emote(roomId, msg)`

---

## Phase 3 — Hot reload with state rebinding

### Preferred approach

**State-external instances** (driver holds state store). On reload:

1. Compile new blueprint version
2. Create new object instance
3. Attach existing `IStateStore`
4. Call optional `OnReload(IMudContext ctx, string oldType)`
5. Swap instance handle

### Unload safety

Only unload old ALC when:
- no instances reference it
- no scheduled callbacks reference its types

---

## Phase 4 — Scheduling & callouts

- `CallOut(targetId, method, delay, args...)`
- `Every(interval, ...)`
- Priority queue + main-loop time budget

---

## Phase 5 — Persistence

Persist:
- players (location/inventory)
- instances + state stores
- container memberships

Start with JSON files, then consider SQLite.

---

## Phase 6 — Multi-user networking

- `ISession` abstraction
- telnet TCP server
- single-threaded game loop + async IO

---

## Phase 7 — Security sandboxing (required if public)

In-process runtime compilation = full trust.
For public use, execute world code out-of-process and expose only a capability-limited API.
