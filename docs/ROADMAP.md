# Roadmap (lpMUD-inspired)

## Phase 1 — Blueprints vs Clones (core lpMUD feel)

- File-backed object = blueprint: `Rooms/meadow.cs`
- Runtime instance = clone: `Rooms/meadow.cs#12345`
- Add commands:
  - `clone <blueprintId>`
  - `destruct <instanceId>`
- Keep instance state separate from blueprint code.

## Phase 2 — Driver hooks

- `Init(Player player)` when a player enters
- `Reset()` periodic / on-demand reset
- `Heartbeat()` scheduled tick
- `CatchTell(string msg)` for room/object messaging

## Phase 3 — Security model (important if exposed)

- Consider separate worker process for untrusted world code
- Restrict API surface (interfaces only)
- Prefer message-passing boundary between kernel and world objects

## Phase 4 — Persistence

- Serialize player + instance state
- Hot reboot without losing world

## Phase 5 — Multi-user networking

- TCP listener (telnet-compatible)
- Session -> Player mapping
- Concurrency strategy for shared world state
