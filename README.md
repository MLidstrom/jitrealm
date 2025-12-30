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
- **Security** (v0.7): Sandboxed world code with blocked dangerous APIs, execution timeouts
- **Living objects** (v0.8): ILiving interface, HP/damage/heal system, World/std/living.cs base class
- **Player as world object** (v0.9): IPlayer interface, cloneable player blueprints, level/XP system
- **Items & Inventory** (v0.10): IItem/ICarryable interfaces, get/drop/inventory commands, weight limits
- **Equipment System** (v0.11): IEquippable/IWeapon/IArmor interfaces, equip/unequip commands, stat bonuses
- **Combat System** (v0.12): ICombatant interface, kill/flee/consider commands, automatic combat rounds
- **NPCs & AI** (v0.13): MonsterBase/NPCBase classes, ISpawner interface, aggressive monsters, friendly NPCs
- **Mudlib Polish** (v0.14): Command registry, social commands (shout/whisper/emotes), help system, RoomBase class
- **Configuration** (v0.15): appsettings.json for driver settings (port, paths, combat, security, player defaults)
- **Item Aliases**: Items can be referenced by multiple names (e.g., "sword", "blade", "weapon")
- **Object Details**: All objects support "look at X" for granular descriptions (e.g., "look at grass")
- **Command Shortcuts**: `l` for look, `n/s/e/w/u/d` for directions

## Requirements

- .NET SDK **8.0+**

## Platforms

JitRealm runs on **Windows**, **Linux**, and **macOS** — anywhere .NET 8 runs.

```bash
# Build for current platform
dotnet build

# Publish self-contained for specific platforms
dotnet publish -c Release -r win-x64 --self-contained
dotnet publish -c Release -r linux-x64 --self-contained
dotnet publish -c Release -r osx-x64 --self-contained
dotnet publish -c Release -r osx-arm64 --self-contained  # Apple Silicon
```

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
- `look` / `l` — show current room
- `look at <detail>` / `l <detail>` — examine room detail (grass, walls, etc.)
- `go <exit>` — move via an exit (triggers IOnLeave/IOnEnter hooks)
- `n` / `s` / `e` / `w` / `u` / `d` — direction shortcuts (north/south/east/west/up/down)
- `quit` — exit

### Items & Inventory
- `get <item>` / `take <item>` — pick up an item from the room (items have aliases, e.g., "sword" or "blade")
- `drop <item>` — drop an item to the room
- `inventory` / `inv` / `i` — list carried items with weights
- `examine <item>` / `exam` / `x` — show item's detailed description
- `score` — show player stats (HP, Level, XP)

### Equipment
- `equip <item>` / `wield` / `wear` — equip an item from inventory
- `unequip <slot>` / `remove` — unequip item from a slot
- `equipment` / `eq` — show equipped items with stats

### Combat
- `kill <target>` / `attack` — start combat with a target
- `flee` / `retreat` — attempt to escape combat (50% chance)
- `consider <target>` / `con` — estimate target difficulty

### Social
- `say <message>` — speak to the room
- `shout <message>` / `yell` — shout to adjacent rooms
- `whisper <player> <message>` / `tell` — private message
- `who` — list online players
- `bow`, `wave`, `laugh`, `smile`, `nod`, `shrug`, `sigh`, `cheer`, `think`, `cry`, `dance`, `yawn` — emotes

### Utility
- `help [command]` — show command help
- `score` — show detailed player stats
- `time` — show server time and playtime

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

## Configuration

Edit `appsettings.json` to customize driver settings:

```json
{
  "Server": {
    "Port": 4000,
    "MaxConnections": 0,
    "WelcomeMessage": "Welcome to JitRealm, {PlayerName}!",
    "MudName": "JitRealm",
    "Version": "0.15"
  },
  "Paths": {
    "WorldDirectory": "World",
    "SaveDirectory": "save",
    "StartRoom": "Rooms/start",
    "PlayerBlueprint": "std/player"
  },
  "GameLoop": {
    "LoopDelayMs": 50,
    "DefaultHeartbeatSeconds": 2,
    "AutoSaveEnabled": false,
    "AutoSaveIntervalMinutes": 15
  },
  "Combat": {
    "RoundIntervalSeconds": 3,
    "FleeChancePercent": 50
  },
  "Security": {
    "HookTimeoutMs": 5000,
    "HeartbeatTimeoutMs": 1000
  },
  "Player": {
    "StartingHP": 100,
    "CarryCapacity": 100,
    "RegenPerHeartbeat": 1
  }
}
```

Command-line arguments override config file settings (e.g., `--port 23`).

## Roadmap & driver plan

- High-level roadmap: [docs/ROADMAP.md](docs/ROADMAP.md)
- Detailed implementation plan: [docs/DRIVER_PLAN.md](docs/DRIVER_PLAN.md)

## License

MIT — see [LICENSE](LICENSE).
