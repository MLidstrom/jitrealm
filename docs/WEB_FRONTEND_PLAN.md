# JitRealm Web Frontend Implementation Plan

This document details the implementation of a modern web frontend for JitRealm, replacing telnet with WebSocket and providing wizard tools for world building.

## Overview

```
┌─────────────────────┐         ┌─────────────────────────────────────┐
│   SvelteKit App     │◄──WS───►│  JitRealm C# Server                 │
│   (TypeScript)      │         │                                     │
├─────────────────────┤         ├─────────────────────────────────────┤
│ - Game Terminal     │         │ - WebSocket Server (new)            │
│ - Stats Panel       │         │ - JSON Protocol Handler             │
│ - Wizard Editor*    │         │ - File API (wizard only)            │
│ - File Explorer*    │         │ - Existing: Telnet, Game Loop       │
└─────────────────────┘         └─────────────────────────────────────┘
                                 * = wizard-only features
```

## Architecture Decisions

### Authentication
- Same credentials as MUD login (player name)
- Wizard status stored in IPlayer (new `IsWizard` property)
- Session token returned after auth, used for subsequent requests

### Protocol
- JSON over WebSocket for all communication
- Message types: `command`, `event`, `file_*`, `auth_*`
- Server pushes game events (room changes, combat, messages)

### Security
- All wizard endpoints check `IsWizard` before executing
- File operations restricted to `World/` directory
- Rate limiting on commands

---

## Phase 1: Backend Foundation

### 1.1 Add IsWizard to Player System

**Files to modify:**
- `Mud/IPlayer.cs` - Add `bool IsWizard { get; }`
- `World/std/player.cs` - Implement IsWizard from state store

```csharp
// Mud/IPlayer.cs
public interface IPlayer : ILiving, IHasInventory, IHasEquipment
{
    // ... existing properties ...

    /// <summary>Whether this player has wizard privileges.</summary>
    bool IsWizard { get; }
}

// World/std/player.cs
public bool IsWizard => _ctx?.State.Get<bool>("isWizard") ?? false;
```

**New command:** `wizard <playername>` (admin only, sets wizard flag)

### 1.2 WebSocket Server Infrastructure

**New files to create:**
```
Mud/Network/
├── WebSocketServer.cs       # Accept WS connections
├── WebSocketSession.cs      # ISession implementation for WS
├── Protocol/
│   ├── MessageTypes.cs      # Enum of message types
│   ├── ClientMessage.cs     # Incoming message structure
│   ├── ServerMessage.cs     # Outgoing message structure
│   └── MessageHandler.cs    # Route messages to handlers
```

**WebSocket Server:**
```csharp
// Mud/Network/WebSocketServer.cs
public sealed class WebSocketServer
{
    private readonly HttpListener _listener;
    private readonly int _port;

    public WebSocketServer(int port = 8080) { }

    public async Task StartAsync(CancellationToken ct);
    public event Action<WebSocketSession> OnClientConnected;
}
```

**WebSocket Session:**
```csharp
// Mud/Network/WebSocketSession.cs
public sealed class WebSocketSession : ISession
{
    public string? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public bool IsWizard { get; private set; }

    public async Task SendAsync(ServerMessage message);
    public async Task<ClientMessage?> ReceiveAsync();
}
```

### 1.3 JSON Protocol Definition

**Message Types:**
```csharp
// Mud/Network/Protocol/MessageTypes.cs
public enum ClientMessageType
{
    // Authentication
    Auth_Login,           // { name: string }

    // Game commands
    Command,              // { command: string }

    // Wizard-only
    File_List,            // { path: string }
    File_Read,            // { path: string }
    File_Write,           // { path: string, content: string }
    Blueprint_Reload,     // { blueprintId: string }
    Blueprint_List,       // { }
    Object_Stat,          // { objectId: string }
}

public enum ServerMessageType
{
    // Authentication
    Auth_Success,         // { playerId, playerName, isWizard }
    Auth_Failed,          // { reason: string }

    // Game events
    Room_Look,            // { name, description, exits, contents }
    Message,              // { type: "say"|"tell"|"emote", from, text }
    Combat_Round,         // { attacker, defender, damage, hp }
    Player_Stats,         // { hp, maxHp, level, xp }

    // Wizard responses
    File_List_Result,     // { files: string[] }
    File_Content,         // { path, content }
    File_Saved,           // { path, success }
    Blueprint_Reloaded,   // { blueprintId, success, error? }
    Object_Info,          // { id, type, state }

    // Errors
    Error,                // { code, message }
}
```

**Client Message Structure:**
```csharp
// Mud/Network/Protocol/ClientMessage.cs
public sealed class ClientMessage
{
    public ClientMessageType Type { get; set; }
    public JsonElement? Payload { get; set; }
}
```

**Server Message Structure:**
```csharp
// Mud/Network/Protocol/ServerMessage.cs
public sealed class ServerMessage
{
    public ServerMessageType Type { get; set; }
    public object? Payload { get; set; }

    public string ToJson();
}
```

### 1.4 Message Handler with Wizard Checks

```csharp
// Mud/Network/Protocol/MessageHandler.cs
public sealed class MessageHandler
{
    private readonly WorldState _state;

    public async Task<ServerMessage> HandleAsync(
        WebSocketSession session,
        ClientMessage message)
    {
        return message.Type switch
        {
            // Public endpoints
            ClientMessageType.Auth_Login => HandleLogin(session, message),
            ClientMessageType.Command => HandleCommand(session, message),

            // Wizard-only endpoints
            ClientMessageType.File_List => RequireWizard(session, () => HandleFileList(message)),
            ClientMessageType.File_Read => RequireWizard(session, () => HandleFileRead(message)),
            ClientMessageType.File_Write => RequireWizard(session, () => HandleFileWrite(message)),
            ClientMessageType.Blueprint_Reload => RequireWizard(session, () => HandleReload(message)),

            _ => ServerMessage.Error("Unknown message type")
        };
    }

    private ServerMessage RequireWizard(WebSocketSession session, Func<ServerMessage> handler)
    {
        if (!session.IsWizard)
            return ServerMessage.Error("Wizard privileges required");
        return handler();
    }
}
```

### 1.5 File Operations API (Wizard Only)

```csharp
// Mud/Network/Protocol/FileOperations.cs
public sealed class FileOperations
{
    private readonly string _worldPath;

    public FileOperations(string worldPath)
    {
        _worldPath = Path.GetFullPath(worldPath);
    }

    public IEnumerable<FileEntry> ListDirectory(string relativePath)
    {
        var fullPath = GetSafePath(relativePath);
        // Return files and directories
    }

    public string ReadFile(string relativePath)
    {
        var fullPath = GetSafePath(relativePath);
        return File.ReadAllText(fullPath);
    }

    public void WriteFile(string relativePath, string content)
    {
        var fullPath = GetSafePath(relativePath);
        File.WriteAllText(fullPath, content);
    }

    private string GetSafePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_worldPath, relativePath));
        if (!fullPath.StartsWith(_worldPath))
            throw new SecurityException("Path traversal attempt");
        return fullPath;
    }
}
```

### 1.6 Integrate WebSocket into Program.cs

```csharp
// Program.cs additions
if (args.Contains("--web"))
{
    var webPort = GetArgValue(args, "--web-port", 8080);
    var wsServer = new WebSocketServer(webPort);
    var webGameServer = new WebGameServer(state, wsServer);
    await webGameServer.RunAsync(cts.Token);
}
```

---

## Phase 2: Game Event Broadcasting

### 2.1 Modify Message Routing

Currently messages go to TelnetSession. Need to also route to WebSocketSession.

**Modify:**
- `Mud/MudContext.cs` - Route messages to all session types
- `Mud/MessageQueue.cs` - Support WebSocket sessions

### 2.2 Push Events to Clients

When game state changes, push updates:

| Event | When | Message Type |
|-------|------|--------------|
| Room changed | Player moves | `Room_Look` |
| Combat round | Every 3s in combat | `Combat_Round` |
| HP changed | Damage/heal | `Player_Stats` |
| Message received | Say/tell/emote | `Message` |
| Item picked up | get command | `Inventory_Update` |

### 2.3 WebGame Server Loop

```csharp
// Mud/Network/WebGameServer.cs
public sealed class WebGameServer
{
    public async Task RunAsync(CancellationToken ct)
    {
        await _wsServer.StartAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            // Process incoming WebSocket messages
            foreach (var session in _sessions)
            {
                var message = await session.TryReceiveAsync();
                if (message != null)
                {
                    var response = await _handler.HandleAsync(session, message);
                    await session.SendAsync(response);
                }
            }

            // Process game loop (heartbeats, combat, callouts)
            await ProcessGameTickAsync();

            // Push state updates to all connected clients
            await BroadcastUpdatesAsync();

            await Task.Delay(100, ct);
        }
    }
}
```

---

## Phase 3: SvelteKit Frontend Foundation

### 3.1 Project Setup

```bash
# Create SvelteKit project in web/ directory
cd JitRealm
npx sv create web
cd web
npm install
```

**Directory structure:**
```
web/
├── src/
│   ├── lib/
│   │   ├── stores/
│   │   │   ├── auth.ts          # Auth state (playerId, isWizard)
│   │   │   ├── game.ts          # Game state (room, inventory)
│   │   │   └── connection.ts    # WebSocket connection
│   │   ├── components/
│   │   │   ├── Terminal.svelte  # Game output (xterm.js)
│   │   │   ├── CommandInput.svelte
│   │   │   ├── StatsPanel.svelte
│   │   │   └── wizard/          # Wizard-only components
│   │   │       ├── FileExplorer.svelte
│   │   │       ├── CodeEditor.svelte
│   │   │       └── ObjectInspector.svelte
│   │   ├── protocol/
│   │   │   ├── types.ts         # Message type definitions
│   │   │   └── client.ts        # WebSocket client wrapper
│   │   └── utils/
│   ├── routes/
│   │   ├── +layout.svelte       # Main layout with auth check
│   │   ├── +page.svelte         # Login page
│   │   └── game/
│   │       └── +page.svelte     # Main game interface
│   └── app.html
├── package.json
├── svelte.config.js
└── tsconfig.json
```

### 3.2 Dependencies

```json
{
  "dependencies": {
    "@monaco-editor/loader": "^1.4.0",
    "svelte-splitpanes": "^8.0.0",
    "xterm": "^5.3.0",
    "xterm-addon-fit": "^0.8.0",
    "bits-ui": "^0.21.0"
  }
}
```

### 3.3 WebSocket Client

```typescript
// src/lib/protocol/client.ts
import { writable } from 'svelte/store';
import type { ClientMessage, ServerMessage } from './types';

export class MudClient {
    private ws: WebSocket | null = null;
    public connected = writable(false);
    public messages = writable<ServerMessage[]>([]);

    connect(url: string): void {
        this.ws = new WebSocket(url);
        this.ws.onopen = () => this.connected.set(true);
        this.ws.onmessage = (e) => this.handleMessage(JSON.parse(e.data));
        this.ws.onclose = () => this.connected.set(false);
    }

    send(message: ClientMessage): void {
        this.ws?.send(JSON.stringify(message));
    }

    login(name: string): void {
        this.send({ type: 'Auth_Login', payload: { name } });
    }

    command(cmd: string): void {
        this.send({ type: 'Command', payload: { command: cmd } });
    }

    // Wizard methods
    listFiles(path: string): void {
        this.send({ type: 'File_List', payload: { path } });
    }

    readFile(path: string): void {
        this.send({ type: 'File_Read', payload: { path } });
    }

    writeFile(path: string, content: string): void {
        this.send({ type: 'File_Write', payload: { path, content } });
    }

    reloadBlueprint(blueprintId: string): void {
        this.send({ type: 'Blueprint_Reload', payload: { blueprintId } });
    }
}
```

### 3.4 Auth Store with Wizard Flag

```typescript
// src/lib/stores/auth.ts
import { writable, derived } from 'svelte/store';

interface AuthState {
    authenticated: boolean;
    playerId: string | null;
    playerName: string | null;
    isWizard: boolean;
}

export const auth = writable<AuthState>({
    authenticated: false,
    playerId: null,
    playerName: null,
    isWizard: false
});

export const isWizard = derived(auth, $auth => $auth.isWizard);
```

---

## Phase 4: Player UI (Everyone)

### 4.1 Main Game Layout

```svelte
<!-- src/routes/game/+page.svelte -->
<script lang="ts">
    import { Splitpanes, Pane } from 'svelte-splitpanes';
    import Terminal from '$lib/components/Terminal.svelte';
    import CommandInput from '$lib/components/CommandInput.svelte';
    import StatsPanel from '$lib/components/StatsPanel.svelte';
    import { isWizard } from '$lib/stores/auth';

    // Wizard components (lazy loaded)
    import WizardTabs from '$lib/components/wizard/WizardTabs.svelte';
</script>

<div class="game-container">
    {#if $isWizard}
        <WizardTabs />
    {/if}

    <Splitpanes>
        <Pane size={70}>
            <div class="terminal-container">
                <Terminal />
                <CommandInput />
            </div>
        </Pane>
        <Pane size={30}>
            <StatsPanel />
        </Pane>
    </Splitpanes>
</div>
```

### 4.2 Terminal Component (xterm.js)

```svelte
<!-- src/lib/components/Terminal.svelte -->
<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { Terminal } from 'xterm';
    import { FitAddon } from 'xterm-addon-fit';
    import { gameMessages } from '$lib/stores/game';

    let terminalEl: HTMLDivElement;
    let term: Terminal;

    onMount(() => {
        term = new Terminal({
            theme: {
                background: '#1a1a2e',
                foreground: '#eee'
            }
        });
        const fitAddon = new FitAddon();
        term.loadAddon(fitAddon);
        term.open(terminalEl);
        fitAddon.fit();

        // Subscribe to game messages
        gameMessages.subscribe(msg => {
            if (msg) term.writeln(formatMessage(msg));
        });
    });

    function formatMessage(msg: ServerMessage): string {
        // Format with ANSI colors based on message type
    }
</script>

<div bind:this={terminalEl} class="terminal"></div>
```

### 4.3 Stats Panel

```svelte
<!-- src/lib/components/StatsPanel.svelte -->
<script lang="ts">
    import { playerStats } from '$lib/stores/game';
</script>

<div class="stats-panel">
    <h3>Character</h3>
    <div class="stat">
        <span>HP</span>
        <div class="bar">
            <div class="fill" style="width: {($playerStats.hp / $playerStats.maxHp) * 100}%"></div>
        </div>
        <span>{$playerStats.hp}/{$playerStats.maxHp}</span>
    </div>
    <div class="stat">
        <span>Level</span>
        <span>{$playerStats.level}</span>
    </div>
    <div class="stat">
        <span>XP</span>
        <span>{$playerStats.xp}</span>
    </div>
</div>
```

---

## Phase 5: Wizard UI (Wizard Only)

### 5.1 Wizard Tabs

```svelte
<!-- src/lib/components/wizard/WizardTabs.svelte -->
<script lang="ts">
    import { Tabs } from 'bits-ui';
    import FileExplorer from './FileExplorer.svelte';
    import CodeEditor from './CodeEditor.svelte';
    import ObjectInspector from './ObjectInspector.svelte';

    let activeTab = 'game';
</script>

<Tabs.Root bind:value={activeTab}>
    <Tabs.List>
        <Tabs.Trigger value="game">Game</Tabs.Trigger>
        <Tabs.Trigger value="editor">World Editor</Tabs.Trigger>
        <Tabs.Trigger value="objects">Objects</Tabs.Trigger>
    </Tabs.List>

    <Tabs.Content value="editor">
        <Splitpanes>
            <Pane size={25}>
                <FileExplorer />
            </Pane>
            <Pane size={75}>
                <CodeEditor />
            </Pane>
        </Splitpanes>
    </Tabs.Content>

    <Tabs.Content value="objects">
        <ObjectInspector />
    </Tabs.Content>
</Tabs.Root>
```

### 5.2 File Explorer

```svelte
<!-- src/lib/components/wizard/FileExplorer.svelte -->
<script lang="ts">
    import { client } from '$lib/protocol/client';
    import { fileTree, selectedFile } from '$lib/stores/wizard';

    function toggleFolder(path: string) {
        client.listFiles(path);
    }

    function openFile(path: string) {
        selectedFile.set(path);
        client.readFile(path);
    }
</script>

<div class="file-explorer">
    <div class="header">
        <span>World Files</span>
        <button on:click={() => client.listFiles('.')}>Refresh</button>
    </div>
    <ul class="tree">
        {#each $fileTree as item}
            {#if item.isDirectory}
                <li class="folder" on:click={() => toggleFolder(item.path)}>
                    {item.name}/
                </li>
            {:else}
                <li class="file" on:click={() => openFile(item.path)}>
                    {item.name}
                </li>
            {/if}
        {/each}
    </ul>
</div>
```

### 5.3 Code Editor (Monaco)

```svelte
<!-- src/lib/components/wizard/CodeEditor.svelte -->
<script lang="ts">
    import { onMount } from 'svelte';
    import loader from '@monaco-editor/loader';
    import { client } from '$lib/protocol/client';
    import { selectedFile, fileContent } from '$lib/stores/wizard';

    let editorContainer: HTMLDivElement;
    let editor: any;
    let modified = false;

    onMount(async () => {
        const monaco = await loader.init();
        editor = monaco.editor.create(editorContainer, {
            language: 'csharp',
            theme: 'vs-dark',
            automaticLayout: true
        });

        editor.onDidChangeModelContent(() => {
            modified = true;
        });
    });

    $: if (editor && $fileContent) {
        editor.setValue($fileContent);
        modified = false;
    }

    function save() {
        if ($selectedFile && editor) {
            client.writeFile($selectedFile, editor.getValue());
            modified = false;
        }
    }

    function reload() {
        if ($selectedFile) {
            client.reloadBlueprint($selectedFile);
        }
    }
</script>

<div class="editor-toolbar">
    <span class="filename">{$selectedFile || 'No file selected'}</span>
    {#if modified}<span class="modified">*</span>{/if}
    <button on:click={save} disabled={!modified}>Save</button>
    <button on:click={reload}>Reload</button>
</div>
<div bind:this={editorContainer} class="editor"></div>
```

---

## Implementation Order

### Sprint 1: Backend WebSocket (Week 1-2)
1. [ ] Add `IsWizard` to IPlayer and PlayerBase
2. [ ] Create WebSocketServer.cs infrastructure
3. [ ] Define JSON protocol types
4. [ ] Implement MessageHandler with auth
5. [ ] Add FileOperations (wizard-gated)
6. [ ] Integrate into Program.cs
7. [ ] Test with simple WebSocket client

### Sprint 2: Game Events (Week 2-3)
1. [ ] Modify message routing for WebSocket
2. [ ] Implement event broadcasting
3. [ ] Create WebGameServer loop
4. [ ] Test full game loop over WebSocket

### Sprint 3: SvelteKit Player UI (Week 3-4)
1. [ ] Scaffold SvelteKit project
2. [ ] Implement WebSocket client
3. [ ] Create auth flow
4. [ ] Build Terminal component
5. [ ] Build StatsPanel
6. [ ] Build CommandInput
7. [ ] Test basic gameplay

### Sprint 4: Wizard UI (Week 4-5)
1. [ ] Add wizard tab system
2. [ ] Build FileExplorer
3. [ ] Integrate Monaco editor
4. [ ] Implement save/reload flow
5. [ ] Build ObjectInspector
6. [ ] Test wizard workflow

### Sprint 5: Polish (Week 5-6)
1. [ ] Error handling and reconnection
2. [ ] Loading states
3. [ ] Responsive design
4. [ ] Performance optimization
5. [ ] Documentation

---

## Security Checklist

- [ ] All wizard endpoints check `session.IsWizard`
- [ ] File paths validated (no traversal outside World/)
- [ ] WebSocket connections authenticated
- [ ] Rate limiting on commands
- [ ] Input sanitization
- [ ] CORS configuration for production

---

## Testing Strategy

### Backend
- Unit tests for MessageHandler
- Unit tests for FileOperations path validation
- Integration tests for WebSocket protocol

### Frontend
- Component tests with Vitest
- E2E tests with Playwright
- Manual wizard workflow testing

---

## Future Enhancements

1. **Syntax validation** - Compile C# in editor before save
2. **Autocomplete** - Monaco intellisense for MUD interfaces
3. **Object graph** - Visual room/exit editor
4. **Live reload** - Auto-reload on save
5. **Collaborative editing** - Multiple wizards editing
6. **Version control** - Git integration for World/
