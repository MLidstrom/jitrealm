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

## Phase 5 — Security model (important if exposed)

- Consider separate worker process for untrusted world code
- Restrict API surface (interfaces only)
- Prefer message-passing boundary between kernel and world objects
