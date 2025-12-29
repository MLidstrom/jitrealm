# JitRealm

A minimal terminal-based MUD kernel in **C# / .NET 8** inspired by **lpMUD**: world objects are **C# source files**
that are compiled and loaded **at runtime**, and can be **unloaded/reloaded** without restarting the server.

## Features

- **World-as-code**: `.cs` files under `World/` are objects in the world
- **Dynamic load/unload**: compile and load objects on demand (Roslyn)
- **Collectible load contexts**: best-effort unload via `AssemblyLoadContext(isCollectible: true)`
- **Hot reload**: edit a world file, then `reload <blueprintId>`
- **Blueprint/instance model** (v0.2): clone blueprints to create unique instances with state
- **Driver hooks** (v0.3): IOnEnter, IOnLeave, IHeartbeat, IResettable, IOnReload
- **Messaging** (v0.3): Tell, Say, Emote via IMudContext
- **Callouts** (v0.4): CallOut, Every, CancelCallOut for scheduled method calls
- **Persistence** (v0.5): Save/load world state to JSON, auto-load on startup
- **Multi-user** (v0.6): Telnet server, multiple concurrent players, sessions

## Requirements

- .NET SDK **8.0+**

## Run

```bash
dotnet restore
dotnet run                        # Single-player console mode
dotnet run -- --server            # Multi-player server (port 4000)
dotnet run -- --server --port 23  # Custom port
```

To connect as a player, use telnet: `telnet localhost 4000`

## Commands

### Navigation
- `look` — show current room
- `go <exit>` — move via an exit (triggers IOnLeave/IOnEnter hooks)
- `quit` — exit

### Object Management
- `objects` — list loaded instances
- `blueprints` — list loaded blueprints
- `clone <blueprintId>` — create a new instance (e.g., `Rooms/meadow.cs#000001`)
- `destruct <objectId>` — remove an instance
- `stat <id>` — show blueprint or instance info
- `reset <objectId>` — trigger IResettable.Reset on an object

### Hot Reload
- `reload <blueprintId>` — recompile and update all instances (preserves state)
- `unload <blueprintId>` — unload blueprint and all its instances

### Persistence
- `save` — save world state to `save/world.json`
- `load` — restore world state from save file

## Roadmap & driver plan

- High-level roadmap: [docs/ROADMAP.md](docs/ROADMAP.md)
- Detailed implementation plan: [docs/DRIVER_PLAN.md](docs/DRIVER_PLAN.md)

## License

MIT — see [LICENSE](LICENSE).
