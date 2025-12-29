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

## Phase 2 — Driver hooks (NEXT)

- `IOnLoad(IMudContext ctx)` — already wired
- `IOnEnter(IMudContext ctx, string whoId)` when a player enters
- `IOnLeave(IMudContext ctx, string whoId)` when a player leaves
- `IHeartbeat()` scheduled tick
- `IResettable.Reset()` periodic / on-demand reset
- Messaging: `Tell`, `Say`, `Emote`

## Phase 3 — Security model (important if exposed)

- Consider separate worker process for untrusted world code
- Restrict API surface (interfaces only)
- Prefer message-passing boundary between kernel and world objects

## Phase 4 — Persistence

- Serialize player + instance state
- Hot reboot without losing world
- JSON files initially, SQLite later

## Phase 5 — Multi-user networking

- TCP listener (telnet-compatible)
- Session -> Player mapping
- Concurrency strategy for shared world state
