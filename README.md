# JitRealm

A minimal terminal-based MUD kernel in **C# / .NET 8** inspired by **lpMUD**: world objects are **C# source files**
that are compiled and loaded **at runtime**, and can be **unloaded/reloaded** without restarting the server.

## Features

- **World-as-code**: `.cs` files under `World/` are objects in the world
- **Dynamic load/unload**: compile and load objects on demand (Roslyn)
- **Collectible load contexts**: best-effort unload via `AssemblyLoadContext(isCollectible: true)`
- **Hot reload**: edit a world file, then `reload <id>`

## Requirements

- .NET SDK **8.0+**

## Run

```bash
dotnet restore
dotnet run
```

## Commands

- `look` — show current room
- `go <exit>` — move via an exit (lazy-loads destination room)
- `objects` — list loaded object IDs
- `reload <objectId>` — unload + recompile + reload an object
- `unload <objectId>` — unload an object
- `quit` — exit

## Roadmap & driver plan

- High-level roadmap: [docs/ROADMAP.md](docs/ROADMAP.md)
- Detailed implementation plan: [docs/DRIVER_PLAN.md](docs/DRIVER_PLAN.md)

## License

MIT — see [LICENSE](LICENSE).
