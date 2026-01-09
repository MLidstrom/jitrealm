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
- **Player Accounts** (v0.16): Login/registration with SHA256 passwords, persistent player data (state, inventory, equipment, location)
- **Wizard Tools** (v0.17): Filesystem navigation (pwd, ls, cd, cat, more, edit), teleportation (goto), wizard homes, performance diagnostics (perf)
- **Unified Commands** (v0.18): All commands use single CommandRegistry system, consistent behavior between console and telnet modes
- **LLM-Powered NPCs**: AI-driven NPC behavior via OpenAI-compatible APIs (configurable), context-aware reactions to room events
- **Persistent NPC Goals + Memory**: optional PostgreSQL-backed per-NPC memory/goals with shared world knowledge base (pgvector-ready)
- **Rich Terminal Output**: ANSI colors via Spectre.Console, toggleable per-session with `colors on|off`
- **Command History**: Up/down arrows navigate command history, with full line editing (left/right, home/end, Ctrl+K/U)
- **Item Aliases**: Items can be referenced by multiple names (e.g., "sword", "blade", "weapon")
- **Object Details**: All livings (players, NPCs, monsters) and items support "look at X" with descriptions and HP
- **Command Shortcuts**: `l` for look, `n/s/e/w/u/d` for directions
- **Local Commands**: Context-sensitive commands from rooms/items (e.g., `buy`/`sell` in shops)
- **Coin System**: Stackable coin objects (GC/SC/CC) with auto-merge, shop transactions, `exchange` command

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
- `look at <target>` / `l <target>` — examine room details, items, NPCs, or players
- `go <exit>` — move via an exit (triggers IOnLeave/IOnEnter hooks)
- `n` / `s` / `e` / `w` / `u` / `d` — direction shortcuts (north/south/east/west/up/down)
- `quit` / `q` — disconnect and save player data

### Items & Inventory
- `get <item>` / `take <item>` — pick up an item from the room (items have aliases, e.g., "sword" or "blade")
- `drop <item>` — drop an item to the room
- `inventory` / `inv` / `i` — list carried items with weights
- `examine <item>` / `exam` / `x` — show item's detailed description
- `read <object>` — read signs, books, scrolls
- `score` — show player stats (HP, Level, XP, Wealth)

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
- `emote <action>` / `me <action>` — custom emote (e.g., "emote looks around")
- `bow`, `wave`, `laugh`, `smile`, `nod`, `shrug`, `sigh`, `cheer`, `think`, `cry`, `dance`, `yawn` — pre-defined emotes

### Utility
- `help` / `?` — show command help (wizard commands shown only for wizards)
- `score` / `sc` — show detailed player stats (HP bar, XP, level, wealth)
- `time` — show server time and playtime
- `colors on|off` — toggle ANSI color codes for terminal output
- `exchange <amount> <coin> to <coin>` — convert between coin types (e.g., `exchange 1 gold to silver`)

### Coins & Economy
- `get 50 gold` / `get all copper` — pick up specific coin amounts
- `drop 25 silver` — drop specific coin amounts
- Coins auto-merge when placed in same container
- Exchange rates: 1 GC = 100 SC = 10000 CC

### Wizard Commands
These commands are visible in `help` and executable only for wizard users:
- `blueprints` — list loaded blueprints
- `objects` — list loaded instances
- `clone <blueprintId>` — create a new instance in current room
- `destruct <objectId>` — remove an instance
- `stat <id>` — show blueprint or instance info
- `reset <objectId>` — trigger IResettable.Reset on an object
- `reload <blueprintId>` — recompile and update all instances (preserves state)
- `unload <blueprintId>` — unload blueprint and all its instances
- `patch <objectId> [key] [value]` — view or modify object state at runtime
- `goto <home|room-id>` — teleport to home room or any room by ID
- `pwd` — print current working directory (within World/)
- `ls [path]` — list directory contents
- `cd <path>` — change working directory
- `cat <file>` — display file with line numbers
- `more <file> [start] [lines]` — display file with paging
- `edit <file>` — nano-style in-game file editor (Ctrl+O save, Ctrl+X exit)
- `ledit <file> [line# [text]]` — line-based editor (no ANSI required, see below)
- `perf` — show driver loop timings and performance stats
- `where <id|name|alias>` — find where an object is located (aliases: locate, find)
- `save` — save world state to `save/world.json`
- `load` — restore world state from save file

### Wizard Filesystem
Wizards can navigate the `World/` directory using Unix-like commands:
- Root `/` = `World/`
- Paths support `.` (current) and `..` (parent)
- Wizards cannot navigate outside `World/`

### Line Editor (ledit)
The `ledit` command provides line-based file editing for terminals without full ANSI support:
- `ledit <file>` — display file with line numbers
- `ledit <file> <line#>` — show specific line
- `ledit <file> <line#> <text>` — replace line with text
- `ledit <file> +<line#> <text>` — insert text after line (use +0 for beginning)
- `ledit <file> -<line#>` — delete line
- `ledit <file> append <text>` — append line at end (creates file if needed)

**Examples:**
```
ledit start.cs              # Show start.cs with line numbers
ledit start.cs 5            # Show line 5
ledit start.cs 5 // comment # Replace line 5 with "// comment"
ledit start.cs +5 new line  # Insert "new line" after line 5
ledit start.cs -5           # Delete line 5
ledit start.cs append }     # Append "}" at end
```

### Wizard Homes
Wizards can have personal home rooms at `World/Rooms/Homes/{letter}/{name}/home.cs`:
- `goto home` teleports the wizard to their home room
- Example: Wizard "Mats" has home at `World/Rooms/Homes/m/mats/home.cs`

### Terminal Line Editing
For ANSI-capable clients, the server provides readline-style line editing:
- **Up/Down arrows**: Navigate command history
- **Left/Right arrows**: Move cursor within line
- **Home/End** or **Ctrl+A/Ctrl+E**: Jump to start/end of line
- **Ctrl+K**: Delete from cursor to end of line
- **Ctrl+U**: Clear entire line
- **Backspace**: Delete character before cursor

## Configuration

Edit `appsettings.json` to customize driver settings:

```json
{
  "Server": {
    "Port": 4000,
    "MaxConnections": 0,
    "WelcomeMessage": "Welcome to JitRealm, {PlayerName}!",
    "MudName": "JitRealm",
    "Version": "0.20"
  },
  "Paths": {
    "WorldDirectory": "World",
    "SaveDirectory": "save",
    "PlayersDirectory": "players",
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
    "RegenPerHeartbeat": 1,
    "XpMultiplier": 1.5,
    "BaseXpPerLevel": 100
  },
  "Memory": {
    "Enabled": false,
    "ConnectionString": "",
    "UsePgvector": true,
    "EmbeddingDimensions": 1536,
    "MaxWriteQueue": 10000,
    "MaxWritesPerSecond": 200,
    "CandidateLimit": 500,
    "DefaultMemoryTopK": 10,
    "DefaultKbTopK": 5
  }
}
```

Command-line arguments override config file settings (e.g., `--port 23`).

## Roadmap & driver plan

- High-level roadmap: [docs/ROADMAP.md](docs/ROADMAP.md)
- Detailed implementation plan: [docs/DRIVER_PLAN.md](docs/DRIVER_PLAN.md)

## License

MIT — see [LICENSE](LICENSE).
