using System.Text.Json;
using System.Diagnostics;
using JitRealm.Mud.AI;
using JitRealm.Mud.Configuration;
using JitRealm.Mud.Diagnostics;
using JitRealm.Mud.Formatting;
using JitRealm.Mud.Persistence;
using JitRealm.Mud.Players;
using JitRealm.Mud.Security;
using Spectre.Console;

namespace JitRealm.Mud.Network;

/// <summary>
/// Main game server handling multi-player connections.
/// </summary>
public sealed class GameServer
{
    private readonly WorldState _state;
    private readonly WorldStatePersistence _persistence;
    private readonly TelnetServer _telnet;
    private readonly DriverSettings _settings;
    private readonly PlayerAccountService _accounts;
    private readonly IClock _clock;
    private readonly LocalCommandDispatcher _localCommands;
    private bool _running;
    private CancellationToken _cancellationToken;

    /// <summary>
    /// Track logged-in player names to prevent duplicate logins.
    /// </summary>
    private readonly HashSet<string> _loggedInPlayers = new(StringComparer.OrdinalIgnoreCase);

    public GameServer(WorldState state, WorldStatePersistence persistence, DriverSettings settings)
    {
        _state = state;
        _persistence = persistence;
        _settings = settings;
        _clock = state.Clock;
        _accounts = new PlayerAccountService(settings);
        _localCommands = new LocalCommandDispatcher(state);
        _telnet = new TelnetServer(settings.Server.Port);

        _telnet.OnClientConnected += OnClientConnected;
    }

    public int Port => _telnet.Port;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _telnet.Start();
        _running = true;
        _cancellationToken = cancellationToken;

        Console.WriteLine($"{_settings.Server.MudName} v{_settings.Server.Version} - Multi-user server");
        Console.WriteLine($"Listening on port {_telnet.Port}...");
        Console.WriteLine($"Players directory: {_accounts.PlayersDirectory}");
        Console.WriteLine("Press Ctrl+C to stop.");

        try
        {
            while (_running && !cancellationToken.IsCancellationRequested)
            {
                var tickStart = Stopwatch.GetTimestamp();

                // Accept any pending connections
                await _telnet.AcceptPendingConnectionsAsync();

                // Process heartbeats (world-wide)
                var hbStart = Stopwatch.GetTimestamp();
                var dueHeartbeats = ProcessHeartbeats();
                var hbTicks = Stopwatch.GetTimestamp() - hbStart;

                // Process callouts (world-wide)
                var coStart = Stopwatch.GetTimestamp();
                var dueCallouts = ProcessCallOuts();
                var coTicks = Stopwatch.GetTimestamp() - coStart;

                // Process combat rounds (world-wide)
                var combatStart = Stopwatch.GetTimestamp();
                ProcessCombat();
                var combatTicks = Stopwatch.GetTimestamp() - combatStart;

                // Process input from all sessions
                var inputStart = Stopwatch.GetTimestamp();
                await ProcessAllSessionsAsync();
                var inputTicks = Stopwatch.GetTimestamp() - inputStart;

                // Deliver messages to sessions
                var deliverStart = Stopwatch.GetTimestamp();
                DeliverMessages();
                var deliverTicks = Stopwatch.GetTimestamp() - deliverStart;

                // Prune disconnected sessions
                _state.Sessions.PruneDisconnected();

                // Small delay to prevent busy-loop
                var totalTicks = Stopwatch.GetTimestamp() - tickStart;
                _state.Metrics.RecordTick(
                    heartbeatsTicks: hbTicks,
                    dueHeartbeats: dueHeartbeats,
                    calloutsTicks: coTicks,
                    dueCallouts: dueCallouts,
                    combatTicks: combatTicks,
                    inputTicks: inputTicks,
                    deliverTicks: deliverTicks,
                    totalTicks: totalTicks);

                await Task.Delay(_settings.GameLoop.LoopDelayMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            _running = false;
            _telnet.Stop();
            Console.WriteLine("Server stopped.");
        }
    }

    public void Stop()
    {
        _running = false;
    }

    private async void OnClientConnected(TelnetSession session)
    {
        Console.WriteLine($"[{session.SessionId}] Connected");

        try
        {
            // Show centered welcome banner with ASCII art
            await WelcomeScreen.RenderAsync(
                session.WriteRawAsync,
                _settings.Server.Version,
                session.TerminalSize.Width,
                session.TerminalSize.Height,
                session.SupportsAnsi
            );

            // Show login prompts below the banner
            await WelcomeScreen.RenderLoginPromptAsync(
                session.WriteAsync,
                session.WriteLineAsync,
                showCreateHint: !_accounts.AnyPlayersExist()
            );

            // Wait for choice (blocking read for login flow)
            var choice = await session.ReadLineBlockingAsync();
            if (choice is null)
            {
                await session.CloseAsync();
                return;
            }

            choice = choice.Trim().ToLowerInvariant();

            if (choice.StartsWith("c"))
            {
                await HandleCreatePlayerAsync(session);
            }
            else
            {
                await HandleLoginAsync(session);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{session.SessionId}] Login error: {ex.Message}");
            await session.CloseAsync();
        }
    }

    private async Task HandleLoginAsync(TelnetSession session)
    {
        await session.WriteLineAsync("");

        const int maxAttempts = 3;

        // Get player name (retry instead of immediately disconnecting)
        string? name = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await session.WriteAsync("Enter player name: ");
            name = await session.ReadLineBlockingAsync();
            if (string.IsNullOrWhiteSpace(name))
            {
                await session.WriteLineAsync("Goodbye!");
                await session.CloseAsync();
                return;
            }
            name = name.Trim();

            if (await _accounts.PlayerExistsAsync(name))
                break;

            name = null;
            await session.WriteLineAsync("Player not found. Try again.");
        }

        if (name is null)
        {
            await session.WriteLineAsync("Too many failed attempts. Disconnecting.");
            await session.CloseAsync();
            return;
        }

        // Check if already logged in
        if (_loggedInPlayers.Contains(name))
        {
            await session.WriteLineAsync("That player is already logged in. Disconnecting.");
            await session.CloseAsync();
            return;
        }

        // Get password (retry instead of immediately disconnecting)
        string? password = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await session.WriteAsync("Enter password: ");
            password = await session.ReadLineBlockingAsync();
            if (string.IsNullOrWhiteSpace(password))
            {
                await session.WriteLineAsync("Goodbye!");
                await session.CloseAsync();
                return;
            }

            if (await _accounts.ValidateCredentialsAsync(name, password))
                break;

            password = null;
            await session.WriteLineAsync("Invalid password. Try again.");
        }

        if (password is null)
        {
            await session.WriteLineAsync("Too many failed attempts. Disconnecting.");
            await session.CloseAsync();
            return;
        }

        // Load player data
        var accountData = await _accounts.LoadPlayerDataAsync(name);
        if (accountData is null)
        {
            await session.WriteLineAsync("Error loading player data. Disconnecting.");
            await session.CloseAsync();
            return;
        }

        // Mark as logged in
        _loggedInPlayers.Add(name);

        // Update last login time
        await _accounts.UpdateLastLoginAsync(name);

        // Create player in world
        await SetupPlayerInWorldAsync(session, accountData);
    }

    private async Task HandleCreatePlayerAsync(TelnetSession session)
    {
        await session.WriteLineAsync("");

        const int maxAttempts = 3;

        // Get player name (retry instead of disconnecting on typos)
        string? name = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await session.WriteAsync("Enter player name: ");
            name = await session.ReadLineBlockingAsync();
            if (string.IsNullOrWhiteSpace(name))
            {
                await session.WriteLineAsync("Goodbye!");
                await session.CloseAsync();
                return;
            }
            name = name.Trim();

            var nameError = PlayerAccountService.ValidatePlayerName(name);
            if (nameError is not null)
            {
                await session.WriteLineAsync(nameError);
                name = null;
                continue;
            }

            if (await _accounts.PlayerExistsAsync(name))
            {
                await session.WriteLineAsync("That name is already taken. Try again.");
                name = null;
                continue;
            }

            break;
        }

        if (name is null)
        {
            await session.WriteLineAsync("Too many failed attempts. Disconnecting.");
            await session.CloseAsync();
            return;
        }

        // Get password (retry instead of disconnecting)
        string? password = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await session.WriteAsync("Enter password: ");
            password = await session.ReadLineBlockingAsync();
            if (string.IsNullOrWhiteSpace(password))
            {
                await session.WriteLineAsync("Goodbye!");
                await session.CloseAsync();
                return;
            }

            var passwordError = PlayerAccountService.ValidatePassword(password);
            if (passwordError is not null)
            {
                await session.WriteLineAsync(passwordError);
                password = null;
                continue;
            }

            await session.WriteAsync("Confirm password: ");
            var confirm = await session.ReadLineBlockingAsync();
            if (confirm != password)
            {
                await session.WriteLineAsync("Passwords don't match. Try again.");
                password = null;
                continue;
            }

            break;
        }

        if (password is null)
        {
            await session.WriteLineAsync("Too many failed attempts. Disconnecting.");
            await session.CloseAsync();
            return;
        }

        // Create account
        var accountData = await _accounts.CreateAccountAsync(name, password);

        // Mark as logged in
        _loggedInPlayers.Add(name);

        await session.WriteLineAsync("");
        await session.WriteLineAsync($"Welcome to the realm, {name}!");

        // Create player in world (new player, no saved state)
        await SetupPlayerInWorldAsync(session, accountData);
    }

    private async Task SetupPlayerInWorldAsync(TelnetSession session, PlayerAccountData accountData)
    {
        var playerName = accountData.Name;

        try
        {
            // Determine starting location
            var startRoomId = accountData.Location ?? _settings.Paths.StartRoom;

            // Try to load the saved location, fall back to start room
            IRoom startRoom;
            try
            {
                startRoom = await _state.Objects!.LoadAsync<IRoom>(startRoomId, _state);
            }
            catch
            {
                // Fall back to start room if saved location doesn't exist
                startRoom = await _state.Objects!.LoadAsync<IRoom>(_settings.Paths.StartRoom, _state);
            }

            // Process spawns for the room (and any linked rooms)
            await ProcessRoomAndLinkedSpawnsAsync(startRoom);

            // Clone player from blueprint
            var player = await _state.Objects.CloneAsync<IPlayer>(_settings.Paths.PlayerBlueprint, _state);

            // Set up the session
            session.PlayerId = player.Id;
            session.PlayerName = playerName;
            session.IsWizard = accountData.IsWizard;

            // Restore player state from saved data
            RestorePlayerState(player.Id, accountData);

            // Set player name via context
            var ctx = CreateContextFor(player.Id);
            if (player is PlayerBase playerBase)
            {
                playerBase.SetPlayerName(playerName, ctx);
            }

            // Move player to room
            _state.Containers.Add(startRoom.Id, player.Id);

            // Restore inventory items
            await RestoreInventoryAsync(player.Id, accountData);

            // Give starting coins to new players (no saved inventory)
            if (accountData.Inventory.Count == 0)
            {
                await CreateStartingCoinsAsync(player.Id);
            }

            // Restore equipment
            RestoreEquipment(player.Id, accountData);

            // Register session
            _state.Sessions.Add(session);

            // Call login hook
            player.OnLogin(ctx);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{session.SessionId}] Failed to create player: {ex.Message}");
            _loggedInPlayers.Remove(playerName);
            await session.CloseAsync();
            return;
        }

        // Enable split-screen terminal UI for gameplay
        if (session.SupportsAnsi)
        {
            await session.EnableSplitScreenAsync();

            // Enable line editor with command history (up/down arrows)
            session.EnableLineEditMode();

            // Initial status bar update
            await UpdateSessionStatusBarAsync(session);
        }

        // Welcome message (now routed through split-screen output area)
        var welcomeMsg = _settings.Server.WelcomeMessage.Replace("{PlayerName}", playerName);
        await session.WriteLineAsync(welcomeMsg);
        await session.WriteLineAsync("Type 'help' for commands, 'quit' to disconnect.");
        await session.WriteLineAsync("");

        // Show initial room
        await ShowRoomAsync(session);

        // Show input prompt
        if (session.TerminalUI?.SupportsSplitScreen == true)
        {
            await session.TerminalUI.RenderInputLineAsync("> ");
        }
    }

    /// <summary>
    /// Update the status bar for a session with current player state.
    /// </summary>
    private async Task UpdateSessionStatusBarAsync(ISession session)
    {
        if (session.TerminalUI?.SupportsSplitScreen != true)
            return;

        var playerId = session.PlayerId;
        if (playerId is null) return;

        var player = _state.Objects!.Get<IPlayer>(playerId);
        if (player is null) return;

        var roomId = _state.Containers.GetContainer(playerId);
        var room = roomId is not null ? _state.Objects.Get<IRoom>(roomId) : null;

        // Get combat info if in combat
        string? combatTarget = null;
        int? targetHP = null, targetMaxHP = null;

        if (_state.Combat.IsInCombat(playerId))
        {
            var targetId = _state.Combat.GetCombatTarget(playerId);
            if (targetId is not null)
            {
                var target = _state.Objects.Get<ILiving>(targetId);
                if (target is not null)
                {
                    combatTarget = target.Name;
                    targetHP = target.HP;
                    targetMaxHP = target.MaxHP;
                }
            }
        }

        var statusData = new StatusBarData
        {
            PlayerName = session.PlayerName ?? "Unknown",
            Location = room?.Name ?? "Nowhere",
            HP = player.HP,
            MaxHP = player.MaxHP,
            CombatTarget = combatTarget,
            TargetHP = targetHP,
            TargetMaxHP = targetMaxHP,
            IsWizard = session.IsWizard,
            Level = player.Level
        };

        await session.TerminalUI.UpdateStatusBarAsync(statusData);
    }

    private void RestorePlayerState(string playerId, PlayerAccountData accountData)
    {
        var stateStore = _state.Objects!.GetStateStore(playerId);
        if (stateStore is null) return;

        foreach (var (key, value) in accountData.State)
        {
            // Convert JsonElement to appropriate type
            object? converted = value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var i) => i,
                JsonValueKind.Number when value.TryGetInt64(out var l) => l,
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };

            if (converted is not null)
            {
                stateStore.Set(key, converted);
            }
        }
    }

    private async Task RestoreInventoryAsync(string playerId, PlayerAccountData accountData)
    {
        foreach (var itemId in accountData.Inventory)
        {
            try
            {
                // Get the blueprint ID from the item ID (strip clone number)
                var blueprintId = ObjectId.Parse(itemId).BlueprintPath;

                // Clone a new instance of the item
                var item = await _state.Objects!.CloneAsync<IItem>(blueprintId, _state);
                if (item is not null)
                {
                    // Move item to player inventory
                    _state.Containers.Add(playerId, item.Id);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RestoreInventory] Failed to restore item {itemId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Create starting coins for a new player.
    /// Default: 10 GC + 50 SC (configurable via settings).
    /// </summary>
    private async Task CreateStartingCoinsAsync(string playerId)
    {
        // Create gold coins (10 GC)
        await CoinHelper.AddCoinsAsync(_state, playerId, 10, CoinMaterial.Gold);

        // Create silver coins (50 SC)
        await CoinHelper.AddCoinsAsync(_state, playerId, 50, CoinMaterial.Silver);
    }

    private void RestoreEquipment(string playerId, PlayerAccountData accountData)
    {
        // Get player's inventory after restoration
        var inventory = _state.Containers.GetContents(playerId);

        foreach (var (slotName, savedItemId) in accountData.Equipment)
        {
            if (!Enum.TryParse<EquipmentSlot>(slotName, out var slot))
                continue;

            // Find matching item in inventory by blueprint
            var savedBlueprint = ObjectId.Parse(savedItemId).BlueprintPath;
            var matchingItemId = inventory.FirstOrDefault(id =>
                ObjectId.Parse(id).BlueprintPath == savedBlueprint);

            if (matchingItemId is not null)
            {
                var item = _state.Objects!.Get<IEquippable>(matchingItemId);
                if (item is not null && item.Slot == slot)
                {
                    _state.Equipment.Equip(playerId, slot, matchingItemId);

                    // Call OnEquip hook
                    var itemCtx = CreateContextFor(matchingItemId);
                    item.OnEquip(playerId, itemCtx);
                }
            }
        }
    }

    private async Task ProcessAllSessionsAsync()
    {
        foreach (var session in _state.Sessions.GetAll())
        {
            if (!session.IsConnected) continue;

            try
            {
                var input = await session.ReadLineAsync();
                if (input is null) continue;

                if (string.IsNullOrWhiteSpace(input))
                {
                    // After Enter on an empty line, reset the split-screen input prompt.
                    await RenderPromptAsync(session);
                    continue;
                }

                await ProcessCommandAsync(session, input.Trim());

                // After handling a command, reset the split-screen input prompt.
                await RenderPromptAsync(session);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{session.SessionId}] Error: {ex.Message}");
            }
        }
    }

    private static Task RenderPromptAsync(ISession session)
    {
        if (session.TerminalUI?.SupportsSplitScreen == true)
            return session.TerminalUI.RenderInputLineAsync("> ");

        return Task.CompletedTask;
    }

    private async Task ProcessCommandAsync(ISession session, string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var playerId = session.PlayerId;
        var playerName = session.PlayerName ?? "Someone";

        if (playerId is null)
        {
            await session.WriteLineAsync("Error: No player associated with this session.");
            return;
        }

        var playerLocation = _state.Containers.GetContainer(playerId);

        switch (cmd)
        {
            case "look":
            case "l":
                if (parts.Length == 1)
                {
                    await ShowRoomAsync(session);
                }
                else
                {
                    // "look at X" or "look X"
                    var target = parts[1].ToLowerInvariant() == "at" && parts.Length > 2
                        ? string.Join(" ", parts.Skip(2))
                        : string.Join(" ", parts.Skip(1));
                    await LookAtDetailAsync(session, target);
                }
                break;

            case "go":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: go <exit>");
                    break;
                }
                await GoAsync(session, parts[1]);
                break;

            // Direction shortcuts
            case "n":
            case "north":
                await GoAsync(session, "north");
                break;
            case "s":
            case "south":
                await GoAsync(session, "south");
                break;
            case "e":
            case "east":
                await GoAsync(session, "east");
                break;
            case "w":
            case "west":
                await GoAsync(session, "west");
                break;
            case "u":
            case "up":
                await GoAsync(session, "up");
                break;
            case "d":
            case "down":
                await GoAsync(session, "down");
                break;

            case "say":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: say <message>");
                    break;
                }
                var sayMsg = string.Join(" ", parts.Skip(1));
                BroadcastToRoom(playerLocation!, $"{playerName} says: {sayMsg}", session);
                await session.WriteLineAsync($"You say: {sayMsg}");
                _state.EventLog.Record(playerLocation!, $"{playerName} said: \"{sayMsg}\"");

                // Trigger LLM responses from NPCs in the room
                var speechEvent = new RoomEvent
                {
                    Type = RoomEventType.Speech,
                    ActorId = playerId,
                    ActorName = playerName,
                    Message = sayMsg
                };
                await TriggerNpcRoomEventAsync(speechEvent, playerLocation!);
                break;

            case "emote":
            case "me":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: emote <action>");
                    break;
                }
                var emoteAction = string.Join(" ", parts.Skip(1));
                BroadcastToRoom(playerLocation!, $"{playerName} {emoteAction}", session);
                await session.WriteLineAsync($"You {emoteAction}");
                _state.EventLog.Record(playerLocation!, $"{playerName} {emoteAction}");

                // Trigger LLM responses from NPCs in the room
                var emoteEvent = new RoomEvent
                {
                    Type = RoomEventType.Emote,
                    ActorId = playerId,
                    ActorName = playerName,
                    Message = emoteAction
                };
                await TriggerNpcRoomEventAsync(emoteEvent, playerLocation!);
                break;

            case "who":
                await ShowWhoAsync(session);
                break;

            case "score":
            case "sc":
                await ShowScoreAsync(session);
                break;

            case "time":
            case "date":
                await ShowTimeAsync(session);
                break;

            case "colors":
            case "colour":
            case "color":
                await ToggleColorsAsync(session, parts.Length > 1 ? parts[1] : null);
                break;

            case "get":
            case "take":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: get <item>");
                    break;
                }
                await GetItemAsync(session, string.Join(" ", parts.Skip(1)));
                break;

            case "drop":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: drop <item>");
                    break;
                }
                await DropItemAsync(session, string.Join(" ", parts.Skip(1)));
                break;

            case "inventory":
            case "inv":
            case "i":
                await ShowInventoryAsync(session);
                break;

            case "examine":
            case "exam":
            case "x":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: examine <item>");
                    break;
                }
                await ExamineItemAsync(session, string.Join(" ", parts.Skip(1)));
                break;

            case "read":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: read <object>");
                    break;
                }
                await ReadObjectAsync(session, string.Join(" ", parts.Skip(1)));
                break;

            case "help":
            case "?":
                await session.WriteLineAsync("=== Commands ===");
                await session.WriteLineAsync("Navigation: l[ook] [at <detail>], go <exit>");
                await session.WriteLineAsync("            n/north, s/south, e/east, w/west, u/up, d/down");
                await session.WriteLineAsync("Items:      get/take <item>, drop <item>, i[nventory], x/examine <item>, read <object>");
                await session.WriteLineAsync("Equipment:  equip/wield/wear <item>, unequip/remove <slot>, eq[uipment]");
                await session.WriteLineAsync("Combat:     kill/attack <target>, flee/retreat, consider/con <target>");
                await session.WriteLineAsync("Social:     say <msg>, shout <msg>, whisper <player> <msg>, who");
                await session.WriteLineAsync("Utility:    sc[ore], time, help/?, q[uit]");

                // Show local commands from room/inventory/equipment
                if (playerId != null)
                {
                    var localCmds = _localCommands.GetAvailableCommands(playerId)
                        .GroupBy(x => x.Source)
                        .ToList();

                    foreach (var group in localCmds)
                    {
                        await session.WriteLineAsync("");
                        await session.WriteLineAsync($"=== {group.Key} ===");
                        foreach (var (_, localCmd) in group)
                        {
                            var aliases = localCmd.Aliases.Count > 0 ? $" ({string.Join("/", localCmd.Aliases)})" : "";
                            await session.WriteLineAsync($"  {localCmd.Usage,-20}{aliases} - {localCmd.Description}");
                        }
                    }
                }

                if (session.IsWizard)
                {
                    await session.WriteLineAsync("");
                    await session.WriteLineAsync("=== Wizard Commands ===");
                    await session.WriteLineAsync("Objects:    blueprints, objects, clone <id>, destruct <id>");
                    await session.WriteLineAsync("            stat <id>, reset <id>, reload <id>, unload <id>");
                    await session.WriteLineAsync("State:      patch <id> [key] [value]");
                    await session.WriteLineAsync("Filesystem: pwd, ls/dir [path], cd <path>, cat <file>, more <file> [start] [lines]");
                    await session.WriteLineAsync("            edit <file>  (ANSI editor)");
                    await session.WriteLineAsync("Movement:   goto/go home, goto/go <room-id>");
                    await session.WriteLineAsync("World:      save, load");
                }
                break;

            case "kill":
            case "attack":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: kill <target>");
                    break;
                }
                await StartCombatAsync(session, string.Join(" ", parts.Skip(1)));
                break;

            case "flee":
            case "retreat":
                await AttemptFleeAsync(session);
                break;

            case "consider":
            case "con":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: consider <target>");
                    break;
                }
                await ConsiderTargetAsync(session, string.Join(" ", parts.Skip(1)));
                break;

            case "equip":
            case "wield":
            case "wear":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: equip <item>");
                    break;
                }
                await EquipItemAsync(session, string.Join(" ", parts.Skip(1)));
                break;

            case "unequip":
            case "remove":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: unequip <slot>");
                    break;
                }
                await UnequipSlotAsync(session, string.Join(" ", parts.Skip(1)));
                break;

            case "equipment":
            case "eq":
                await ShowEquipmentAsync(session);
                break;

            // Wizard commands - all require IsWizard
            case "reload":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: reload <blueprintId>");
                    break;
                }
                await _state.Objects!.ReloadBlueprintAsync(parts[1], _state);
                await session.WriteLineAsync($"Reloaded blueprint {parts[1]}");
                break;

            case "unload":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: unload <blueprintId>");
                    break;
                }
                await _state.Objects!.UnloadBlueprintAsync(parts[1], _state);
                await session.WriteLineAsync($"Unloaded blueprint {parts[1]}");
                break;

            case "blueprints":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                await session.WriteLineAsync("=== Blueprints ===");
                foreach (var id in _state.Objects!.ListBlueprintIds())
                    await session.WriteLineAsync($"  {id}");
                break;

            case "objects":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                await session.WriteLineAsync("=== Instances ===");
                foreach (var id in _state.Objects!.ListInstanceIds())
                    await session.WriteLineAsync($"  {id}");
                break;

            case "clone":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: clone <blueprintId>");
                    break;
                }
                var cloned = await _state.Objects!.CloneAsync<IMudObject>(parts[1], _state);
                if (cloned is not null && playerLocation is not null)
                {
                    _state.Containers.Add(playerLocation, cloned.Id);
                    await session.WriteLineAsync($"Cloned {parts[1]} -> {cloned.Id}");
                }
                break;

            case "destruct":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: destruct <objectId>");
                    break;
                }
                await _state.Objects!.DestructAsync(parts[1], _state);
                _state.Containers.Remove(parts[1]);
                await session.WriteLineAsync($"Destructed {parts[1]}");
                break;

            case "stat":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: stat <objectId>");
                    break;
                }
                await ShowStatAsync(session, parts[1]);
                break;

            case "reset":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: reset <objectId>");
                    break;
                }
                await ResetObjectAsync(session, parts[1]);
                break;

            case "patch":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                await HandlePatchAsync(session, parts);
                break;

            case "pwd":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                await session.WriteLineAsync(Commands.Wizard.WizardFilesystem.GetWorkingDir(session.SessionId));
                break;

            case "ls":
            case "dir":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                await HandleLsAsync(session, parts.Skip(1).ToArray());
                break;

            case "cd":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                await HandleCdAsync(session, parts.Skip(1).ToArray());
                break;

            case "cat":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                await HandleCatAsync(session, parts.Skip(1).ToArray());
                break;

            case "more":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                await HandleMoreAsync(session, parts.Skip(1).ToArray());
                break;

            case "edit":
                if (!session.IsWizard) { await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands."); break; }
                await HandleEditAsync(session, parts.Skip(1).ToArray());
                break;

            case "save":
                if (!session.IsWizard)
                {
                    await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands.");
                    break;
                }
                await _persistence.SaveAsync(_state);
                await session.WriteLineAsync("World state saved.");
                break;

            case "load":
                if (!session.IsWizard)
                {
                    await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands.");
                    break;
                }
                await _persistence.LoadAsync(_state);
                await session.WriteLineAsync("World state loaded.");
                break;

            case "quit":
            case "exit":
            case "q":
                Console.WriteLine($"[{session.SessionId}] {playerName} disconnected");

                // Save player data before disconnecting
                await SavePlayerDataAsync(session, playerId);

                // Call logout hook
                var player = _state.Objects!.Get<IPlayer>(playerId);
                if (player is not null)
                {
                    var ctx = CreateContextFor(playerId);
                    player.OnLogout(ctx);
                }

                BroadcastToRoom(playerLocation!, $"{playerName} has left the realm.", session);

                // Remove player from room and clean up inventory items
                CleanupPlayerItems(playerId);
                _state.Containers.Remove(playerId);

                // Remove from logged-in players
                _loggedInPlayers.Remove(playerName);

                await session.CloseAsync();
                _state.Sessions.Remove(session.SessionId);
                break;

            default:
                // Try local commands from room/inventory/equipment before "Unknown command"
                if (playerId != null)
                {
                    var handled = await _localCommands.TryExecuteAsync(
                        playerId,
                        cmd,
                        parts.Skip(1).ToArray(),
                        objId => CreateContextFor(objId));

                    if (handled)
                        break;
                }
                await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands.");
                break;
        }
    }

    /// <summary>
    /// Process spawns for a room and any linked rooms it declares.
    /// </summary>
    private async Task ProcessRoomAndLinkedSpawnsAsync(IRoom room)
    {
        // Process spawns for the main room
        await _state.ProcessSpawnsAsync(room.Id, _clock);

        // Check if this room has linked rooms that also need loading
        if (room is IHasLinkedRooms hasLinkedRooms)
        {
            foreach (var linkedRoomId in hasLinkedRooms.LinkedRooms)
            {
                var linkedRoom = await _state.Objects!.LoadAsync<IRoom>(linkedRoomId, _state);
                await _state.ProcessSpawnsAsync(linkedRoom.Id, _clock);
            }
        }
    }

    private async Task ShowRoomAsync(ISession session)
    {
        var playerId = session.PlayerId;
        var fmt = session.Formatter;

        if (playerId is null)
        {
            await session.WriteLineAsync(fmt.FormatError("You are nowhere."));
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId is null)
        {
            await session.WriteLineAsync(fmt.FormatError("You are nowhere."));
            return;
        }

        var room = _state.Objects!.Get<IRoom>(roomId);
        if (room is null)
        {
            await session.WriteLineAsync(fmt.FormatError("You are nowhere."));
            return;
        }

        await session.WriteLineAsync(""); // Blank line before room name
        await session.WriteLineAsync(fmt.FormatRoomName(room.Name));
        await session.WriteLineAsync(fmt.FormatRoomDescription(room.Description));

        if (room.Exits.Count > 0)
            await session.WriteLineAsync(fmt.FormatExits(room.Exits.Keys));

        // Show other players and objects in room
        var contents = _state.Containers.GetContents(roomId);
        var players = new List<string>();
        var objectNames = new List<string>();

        foreach (var objId in contents)
        {
            if (objId == playerId) continue; // Skip self

            var obj = _state.Objects.Get<IMudObject>(objId);
            if (obj is IPlayer)
            {
                // It's another player - find their name
                var otherSession = _state.Sessions.GetByPlayerId(objId);
                players.Add(otherSession?.PlayerName ?? obj.Name);
            }
            else if (obj is IItem item)
            {
                objectNames.Add(item.ShortDescription);
            }
            else if (obj is not null)
            {
                objectNames.Add(obj.Name);
            }
        }

        if (players.Count > 0)
            await session.WriteLineAsync(fmt.FormatPlayersHere(players));

        if (objectNames.Count > 0)
        {
            var formatted = ItemFormatter.FormatGroupedList(objectNames);
            await session.WriteLineAsync(fmt.FormatObjectsHere(formatted));
        }
    }

    private async Task GoAsync(ISession session, string exit)
    {
        var playerId = session.PlayerId;
        var playerName = session.PlayerName ?? "Someone";

        if (playerId is null)
        {
            await session.WriteLineAsync("You are nowhere.");
            return;
        }

        var currentRoomId = _state.Containers.GetContainer(playerId);
        if (currentRoomId is null)
        {
            await session.WriteLineAsync("You are nowhere.");
            return;
        }

        var currentRoom = _state.Objects!.Get<IRoom>(currentRoomId);
        if (currentRoom is null)
        {
            await session.WriteLineAsync("You are nowhere.");
            return;
        }

        if (!currentRoom.Exits.TryGetValue(exit, out var destId))
        {
            await session.WriteLineAsync("You can't go that way.");
            return;
        }

        // Notify others in current room
        BroadcastToRoom(currentRoomId, $"{playerName} leaves {exit}.", session);
        _state.EventLog.Record(currentRoomId, $"{playerName} left {exit}");

        // Trigger NPC reactions to departure
        var departureEvent = new RoomEvent
        {
            Type = RoomEventType.Departure,
            ActorId = playerId,
            ActorName = playerName,
            Direction = exit
        };
        await TriggerNpcRoomEventAsync(departureEvent, currentRoomId);

        // Move to destination
        var destRoom = await _state.Objects.LoadAsync<IRoom>(destId, _state);

        // Process spawns for the destination room (and any linked rooms)
        await ProcessRoomAndLinkedSpawnsAsync(destRoom);

        _state.Containers.Move(playerId, destRoom.Id);

        // Notify others in new room
        var fromDirection = GetOppositeDirection(exit);
        BroadcastToRoom(destRoom.Id, $"{playerName} arrives from the {fromDirection}.", session);
        _state.EventLog.Record(destRoom.Id, $"{playerName} arrived from the {fromDirection}");

        // Trigger NPC reactions to arrival
        var arrivalEvent = new RoomEvent
        {
            Type = RoomEventType.Arrival,
            ActorId = playerId,
            ActorName = playerName,
            Direction = fromDirection
        };
        await TriggerNpcRoomEventAsync(arrivalEvent, destRoom.Id);

        // Show new room and update status bar with new location
        await ShowRoomAsync(session);
        await UpdateSessionStatusBarAsync(session);
    }

    private async Task ShowWhoAsync(ISession session)
    {
        var sessions = _state.Sessions.GetAll();
        await session.WriteLineAsync($"Players online: {sessions.Count}");
        foreach (var s in sessions)
        {
            if (s.PlayerId is not null)
            {
                var location = _state.Containers.GetContainer(s.PlayerId);
                var roomName = location is not null
                    ? _state.Objects!.Get<IRoom>(location)?.Name ?? "unknown"
                    : "unknown";
                await session.WriteLineAsync($"  {s.PlayerName ?? "Unknown"} - {roomName}");
            }
        }
    }

    private async Task ShowScoreAsync(ISession session)
    {
        var playerId = session.PlayerId;
        var fmt = session.Formatter;

        if (playerId is null)
        {
            await session.WriteLineAsync(fmt.FormatError("No player."));
            return;
        }

        var player = _state.Objects!.Get<IPlayer>(playerId);
        if (player is null)
        {
            await session.WriteLineAsync(fmt.FormatError("Player not found."));
            return;
        }

        // Calculate XP to next level
        var xpForNextLevel = (int)(player.Level * _settings.Player.BaseXpPerLevel * _settings.Player.XpMultiplier);

        await session.WriteLineAsync(fmt.FormatScoreHeader(player.PlayerName, session.IsWizard));
        await session.WriteLineAsync(fmt.FormatLevel(player.Level));
        await session.WriteLineAsync(fmt.FormatHpBar(player.HP, player.MaxHP));
        await session.WriteLineAsync(fmt.FormatXpProgress(player.Experience, xpForNextLevel));
        await session.WriteLineAsync(fmt.FormatCombatStats(player.TotalArmorClass, player.WeaponDamage.min, player.WeaponDamage.max));
        await session.WriteLineAsync(fmt.FormatCarryWeight(player.CarriedWeight, player.CarryCapacity));

        // Display coin breakdown
        var wealth = CoinHelper.FormatWealth(_state, playerId);
        await session.WriteLineAsync($"Wealth: {wealth}");

        await session.WriteLineAsync(fmt.FormatSessionTime(player.SessionTime));
    }

    private async Task ShowTimeAsync(ISession session)
    {
        var now = DateTimeOffset.Now;
        await session.WriteLineAsync($"=== Time ===");
        await session.WriteLineAsync($"Server time: {now:yyyy-MM-dd HH:mm:ss zzz}");

        var playerId = session.PlayerId;
        if (playerId is not null)
        {
            var player = _state.Objects!.Get<IPlayer>(playerId);
            if (player is not null)
            {
                await session.WriteLineAsync($"Session time: {FormatTimeSpan(player.SessionTime)}");

                if (player is PlayerBase playerBase)
                {
                    await session.WriteLineAsync($"Total playtime: {FormatTimeSpan(playerBase.TotalPlaytime)}");
                }
            }
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }

    private void BroadcastToRoom(string roomId, string message, ISession? exclude = null)
    {
        var sessions = _state.Sessions.GetSessionsInRoom(roomId, _state.Containers.GetContainer);
        foreach (var session in sessions)
        {
            if (session == exclude) continue;
            _ = session.WriteLineAsync(message);
        }
    }

    private int ProcessHeartbeats()
    {
        var dueObjects = _state.Heartbeats.GetDueHeartbeats();

        foreach (var objectId in dueObjects)
        {
            var obj = _state.Objects!.Get<IMudObject>(objectId);
            if (obj is IHeartbeat heartbeat)
            {
                var ctx = CreateContextFor(objectId);
                SafeInvoker.TryInvokeHeartbeat(() => heartbeat.Heartbeat(ctx), $"Heartbeat in {objectId}");
            }
        }

        return dueObjects.Count;
    }

    private int ProcessCallOuts()
    {
        var dueCallouts = _state.CallOuts.GetDueCallouts();

        foreach (var callout in dueCallouts)
        {
            var obj = _state.Objects!.Get<IMudObject>(callout.TargetId);
            if (obj is null) continue;

            var ctx = CreateContextFor(callout.TargetId);

            SafeInvoker.TryInvokeCallout(
                () =>
                {
                    if (!CallOutInvoker.TryInvoke(obj, callout, ctx,
                            msg => Console.WriteLine($"[CallOut error in {callout.TargetId}]: {msg}")))
                    {
                        Console.WriteLine($"[CallOut error in {callout.TargetId}]: Method '{callout.MethodName}' not found");
                    }
                },
                $"CallOut {callout.MethodName} in {callout.TargetId}");
        }

        return dueCallouts.Count;
    }

    private void DeliverMessages()
    {
        var messages = _state.Messages.Drain();

        foreach (var msg in messages)
        {
            // Route messages to appropriate sessions
            switch (msg.Type)
            {
                case MessageType.Tell:
                    // Direct message to a player
                    var targetSession = msg.ToId is not null
                        ? _state.Sessions.GetByPlayerId(msg.ToId)
                        : null;
                    if (targetSession is not null)
                    {
                        var formatted = $"{GetObjectName(msg.FromId)} tells you: {msg.Content}";
                        _ = targetSession.WriteLineAsync(formatted);
                    }
                    break;

                case MessageType.Say:
                case MessageType.Emote:
                    // Room broadcast
                    if (msg.RoomId is not null)
                    {
                        var formatted = msg.Type == MessageType.Say
                            ? $"{GetObjectName(msg.FromId)} says: {msg.Content}"
                            : $"{GetObjectName(msg.FromId)} {msg.Content}";

                        var sessions = _state.Sessions.GetSessionsInRoom(msg.RoomId, _state.Containers.GetContainer);
                        foreach (var session in sessions)
                        {
                            _ = session.WriteLineAsync(formatted);
                        }
                    }
                    break;
            }
        }
    }

    private MudContext CreateContextFor(string objectId)
    {
        return _state.CreateContext(objectId);
    }

    private string GetObjectName(string objectId)
    {
        // First check if it's a player - use their name from session
        var session = _state.Sessions.GetByPlayerId(objectId);
        if (session?.PlayerName is not null)
            return session.PlayerName;

        var obj = _state.Objects!.Get<IMudObject>(objectId);
        return obj?.Name ?? objectId;
    }

    private async Task GetItemAsync(ISession session, string itemName)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId is null)
        {
            await session.WriteLineAsync("You're not in a room.");
            return;
        }

        // Check if this is a coin command (e.g., "50 gold", "all silver")
        var coinParse = CoinHelper.ParseCoinCommand(itemName);
        if (coinParse.HasValue)
        {
            await GetCoinsAsync(session, coinParse.Value.Amount, coinParse.Value.Material, roomId);
            return;
        }

        // Check for "all <material>" pattern
        if (itemName.StartsWith("all ", StringComparison.OrdinalIgnoreCase))
        {
            var materialStr = itemName[4..].Trim();
            var material = CoinHelper.ParseMaterial(materialStr);
            if (material.HasValue)
            {
                await GetAllCoinsAsync(session, material.Value, roomId);
                return;
            }
        }

        // Find item in room
        var ctx = CreateContextFor(playerId);
        var itemId = ctx.FindItem(itemName, roomId);
        if (itemId is null)
        {
            await session.WriteLineAsync($"You don't see '{itemName}' here.");
            return;
        }

        // Check if it's a carryable item
        var item = _state.Objects!.Get<IItem>(itemId);
        if (item is null)
        {
            await session.WriteLineAsync("That's not an item.");
            return;
        }

        // Check weight limit
        var player = _state.Objects.Get<IPlayer>(playerId);
        if (player is not null && !player.CanCarry(item.Weight))
        {
            await session.WriteLineAsync($"You can't carry that much weight. (Carrying {player.CarriedWeight}/{player.CarryCapacity})");
            return;
        }

        // Move item to player inventory
        ctx.Move(itemId, playerId);
        await session.WriteLineAsync($"You pick up {item.ShortDescription}.");
        BroadcastToRoom(roomId, $"{session.PlayerName} picks up {item.ShortDescription}.", session);
        _state.EventLog.Record(roomId, $"{session.PlayerName} picked up {item.ShortDescription}");

        // Trigger NPC reactions
        var pickupEvent = new RoomEvent
        {
            Type = RoomEventType.ItemTaken,
            ActorId = playerId,
            ActorName = session.PlayerName ?? "Someone",
            Target = item.ShortDescription
        };
        await TriggerNpcRoomEventAsync(pickupEvent, roomId);
    }

    private async Task DropItemAsync(ISession session, string itemName)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId is null)
        {
            await session.WriteLineAsync("You're not in a room.");
            return;
        }

        // Check if this is a coin command (e.g., "50 gold", "all silver")
        var coinParse = CoinHelper.ParseCoinCommand(itemName);
        if (coinParse.HasValue)
        {
            await DropCoinsAsync(session, coinParse.Value.Amount, coinParse.Value.Material, roomId);
            return;
        }

        // Check for "all <material>" pattern
        if (itemName.StartsWith("all ", StringComparison.OrdinalIgnoreCase))
        {
            var materialStr = itemName[4..].Trim();
            var material = CoinHelper.ParseMaterial(materialStr);
            if (material.HasValue)
            {
                await DropAllCoinsAsync(session, material.Value, roomId);
                return;
            }
        }

        // Find item in inventory
        var ctx = CreateContextFor(playerId);
        var itemId = ctx.FindItem(itemName, playerId);
        if (itemId is null)
        {
            await session.WriteLineAsync($"You're not carrying '{itemName}'.");
            return;
        }

        var item = _state.Objects!.Get<IItem>(itemId);
        if (item is null)
        {
            await session.WriteLineAsync("That's not an item.");
            return;
        }

        // Move item to room
        ctx.Move(itemId, roomId);
        await session.WriteLineAsync($"You drop {item.ShortDescription}.");
        BroadcastToRoom(roomId, $"{session.PlayerName} drops {item.ShortDescription}.", session);
        _state.EventLog.Record(roomId, $"{session.PlayerName} dropped {item.ShortDescription}");

        // Trigger NPC reactions
        var dropEvent = new RoomEvent
        {
            Type = RoomEventType.ItemDropped,
            ActorId = playerId,
            ActorName = session.PlayerName ?? "Someone",
            Target = item.ShortDescription
        };
        await TriggerNpcRoomEventAsync(dropEvent, roomId);
    }

    /// <summary>
    /// Pick up a specific amount of coins from the room.
    /// </summary>
    private async Task GetCoinsAsync(ISession session, int amount, CoinMaterial material, string roomId)
    {
        var playerId = session.PlayerId!;
        var matName = material.ToString().ToLower();

        // Find coin pile in room
        var coinId = CoinHelper.FindCoinPile(_state, roomId, material);
        if (coinId is null)
        {
            await session.WriteLineAsync($"There are no {matName} coins here.");
            return;
        }

        var coin = _state.Objects!.Get<ICoin>(coinId);
        if (coin is null || coin.Amount < amount)
        {
            await session.WriteLineAsync($"There aren't that many {matName} coins here. (Only {coin?.Amount ?? 0})");
            return;
        }

        // Transfer coins
        if (await CoinHelper.TransferCoinsAsync(_state, roomId, playerId, amount, material))
        {
            var desc = CoinHelper.FormatCoins(amount, material);
            await session.WriteLineAsync($"You pick up {desc}.");
            BroadcastToRoom(roomId, $"{session.PlayerName} picks up {desc}.", session);
            _state.EventLog.Record(roomId, $"{session.PlayerName} picked up {desc}");
        }
        else
        {
            await session.WriteLineAsync("Failed to pick up coins.");
        }
    }

    /// <summary>
    /// Pick up all coins of a material from the room.
    /// </summary>
    private async Task GetAllCoinsAsync(ISession session, CoinMaterial material, string roomId)
    {
        var playerId = session.PlayerId!;
        var matName = material.ToString().ToLower();

        // Find coin pile in room
        var coinId = CoinHelper.FindCoinPile(_state, roomId, material);
        if (coinId is null)
        {
            await session.WriteLineAsync($"There are no {matName} coins here.");
            return;
        }

        var coin = _state.Objects!.Get<ICoin>(coinId);
        if (coin is null || coin.Amount <= 0)
        {
            await session.WriteLineAsync($"There are no {matName} coins here.");
            return;
        }

        var amount = coin.Amount;
        var ctx = CreateContextFor(playerId);

        // Move the entire pile (will merge at destination via Move())
        ctx.Move(coinId, playerId);

        var desc = CoinHelper.FormatCoins(amount, material);
        await session.WriteLineAsync($"You pick up {desc}.");
        BroadcastToRoom(roomId, $"{session.PlayerName} picks up {desc}.", session);
        _state.EventLog.Record(roomId, $"{session.PlayerName} picked up {desc}");
    }

    /// <summary>
    /// Drop a specific amount of coins to the room.
    /// </summary>
    private async Task DropCoinsAsync(ISession session, int amount, CoinMaterial material, string roomId)
    {
        var playerId = session.PlayerId!;
        var matName = material.ToString().ToLower();

        // Find coin pile in player inventory
        var coinId = CoinHelper.FindCoinPile(_state, playerId, material);
        if (coinId is null)
        {
            await session.WriteLineAsync($"You don't have any {matName} coins.");
            return;
        }

        var coin = _state.Objects!.Get<ICoin>(coinId);
        if (coin is null || coin.Amount < amount)
        {
            await session.WriteLineAsync($"You don't have that many {matName} coins. (Only {coin?.Amount ?? 0})");
            return;
        }

        // Transfer coins
        if (await CoinHelper.TransferCoinsAsync(_state, playerId, roomId, amount, material))
        {
            var desc = CoinHelper.FormatCoins(amount, material);
            await session.WriteLineAsync($"You drop {desc}.");
            BroadcastToRoom(roomId, $"{session.PlayerName} drops {desc}.", session);
            _state.EventLog.Record(roomId, $"{session.PlayerName} dropped {desc}");
        }
        else
        {
            await session.WriteLineAsync("Failed to drop coins.");
        }
    }

    /// <summary>
    /// Drop all coins of a material to the room.
    /// </summary>
    private async Task DropAllCoinsAsync(ISession session, CoinMaterial material, string roomId)
    {
        var playerId = session.PlayerId!;
        var matName = material.ToString().ToLower();

        // Find coin pile in player inventory
        var coinId = CoinHelper.FindCoinPile(_state, playerId, material);
        if (coinId is null)
        {
            await session.WriteLineAsync($"You don't have any {matName} coins.");
            return;
        }

        var coin = _state.Objects!.Get<ICoin>(coinId);
        if (coin is null || coin.Amount <= 0)
        {
            await session.WriteLineAsync($"You don't have any {matName} coins.");
            return;
        }

        var amount = coin.Amount;
        var ctx = CreateContextFor(playerId);

        // Move the entire pile (will merge at destination via Move())
        ctx.Move(coinId, roomId);

        var desc = CoinHelper.FormatCoins(amount, material);
        await session.WriteLineAsync($"You drop {desc}.");
        BroadcastToRoom(roomId, $"{session.PlayerName} drops {desc}.", session);
        _state.EventLog.Record(roomId, $"{session.PlayerName} dropped {desc}");
    }

    private async Task ShowInventoryAsync(ISession session)
    {
        var playerId = session.PlayerId;
        var fmt = session.Formatter;

        if (playerId is null)
        {
            await session.WriteLineAsync(fmt.FormatError("No player."));
            return;
        }

        var contents = _state.Containers.GetContents(playerId);
        if (contents.Count == 0)
        {
            await session.WriteLineAsync(fmt.FormatInventoryEmpty());
            return;
        }

        await session.WriteLineAsync(fmt.FormatInventoryHeader());
        int totalWeight = 0;

        // Group coins by material (summing amounts only), regular items by description
        var coinTotals = new Dictionary<CoinMaterial, int>();  // material -> total amount
        var itemGroups = new Dictionary<string, (int count, int totalWeight)>(StringComparer.OrdinalIgnoreCase);
        var nonItems = new List<string>();

        foreach (var itemId in contents)
        {
            var item = _state.Objects!.Get<IItem>(itemId);
            if (item is not null)
            {
                // Handle coins specially - group by material and sum amounts only
                if (item is ICoin coin)
                {
                    if (coinTotals.TryGetValue(coin.Material, out var existing))
                    {
                        coinTotals[coin.Material] = existing + coin.Amount;
                    }
                    else
                    {
                        coinTotals[coin.Material] = coin.Amount;
                    }
                    // Don't add to totalWeight here - we'll calculate correct weight later
                }
                else
                {
                    var desc = item.ShortDescription;
                    if (itemGroups.TryGetValue(desc, out var existing))
                    {
                        itemGroups[desc] = (existing.count + 1, existing.totalWeight + item.Weight);
                    }
                    else
                    {
                        itemGroups[desc] = (1, item.Weight);
                    }
                    totalWeight += item.Weight;
                }
            }
            else
            {
                var obj = _state.Objects.Get<IMudObject>(itemId);
                nonItems.Add(obj?.Name ?? itemId);
            }
        }

        // Add correct coin weights to total (0.01 per coin, min 1 lb per material)
        foreach (var (_, amount) in coinTotals)
        {
            totalWeight += Math.Max(1, (int)Math.Ceiling(amount * 0.01));
        }

        // Display coins first (ordered: Gold, Silver, Copper)
        foreach (var material in new[] { CoinMaterial.Gold, CoinMaterial.Silver, CoinMaterial.Copper })
        {
            if (coinTotals.TryGetValue(material, out var amount))
            {
                var matName = material.ToString().ToLower();
                var desc = amount == 1 ? $"1 {matName} coin" : $"{amount} {matName} coins";
                // Weight = 0.01 per coin, minimum 1 lb (matches coin.cs formula)
                var weight = Math.Max(1, (int)Math.Ceiling(amount * 0.01));
                await session.WriteLineAsync(fmt.FormatInventoryItem(desc, weight, 1));
            }
        }

        // Display grouped regular items
        foreach (var (desc, (count, weight)) in itemGroups.OrderBy(kv => kv.Key))
        {
            var displayDesc = count == 1
                ? ItemFormatter.WithArticle(desc)
                : $"{count} {ItemFormatter.Pluralize(desc)}";
            await session.WriteLineAsync(fmt.FormatInventoryItem(displayDesc, weight, count));
        }

        // Display non-item objects (grouped)
        foreach (var formatted in ItemFormatter.FormatGrouped(nonItems))
        {
            await session.WriteLineAsync($"  {formatted}");
        }

        var player = _state.Objects!.Get<IPlayer>(playerId);
        if (player is not null)
        {
            await session.WriteLineAsync(fmt.FormatInventoryTotal(totalWeight, player.CarryCapacity));
        }
    }

    private async Task LookAtDetailAsync(ISession session, string target)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId is null)
        {
            await session.WriteLineAsync("You're not in a room.");
            return;
        }

        var normalizedTarget = target.ToLowerInvariant();

        // 1. Check room details first
        var room = _state.Objects!.Get<IRoom>(roomId);
        if (room is not null)
        {
            foreach (var (keyword, description) in room.Details)
            {
                if (keyword.ToLowerInvariant().Contains(normalizedTarget) ||
                    normalizedTarget.Contains(keyword.ToLowerInvariant()))
                {
                    await session.WriteLineAsync(description);
                    return;
                }
            }
        }

        // 2. Check items in inventory
        var ctx = CreateContextFor(playerId);
        var itemId = ctx.FindItem(target, playerId);
        if (itemId is not null)
        {
            var item = _state.Objects.Get<IItem>(itemId);
            if (item is not null)
            {
                // Check item details first
                foreach (var (keyword, description) in item.Details)
                {
                    if (keyword.ToLowerInvariant().Contains(normalizedTarget) ||
                        normalizedTarget.Contains(keyword.ToLowerInvariant()))
                    {
                        await session.WriteLineAsync(description);
                        return;
                    }
                }
                // Fall back to item long description
                await session.WriteLineAsync(item.LongDescription);
                return;
            }
        }

        // 3. Check items/objects in room
        itemId = ctx.FindItem(target, roomId);
        if (itemId is not null)
        {
            var item = _state.Objects.Get<IItem>(itemId);
            if (item is not null)
            {
                // Check item details first
                foreach (var (keyword, description) in item.Details)
                {
                    if (keyword.ToLowerInvariant().Contains(normalizedTarget) ||
                        normalizedTarget.Contains(keyword.ToLowerInvariant()))
                    {
                        await session.WriteLineAsync(description);
                        return;
                    }
                }
                // Fall back to item long description
                await session.WriteLineAsync(item.LongDescription);
                return;
            }
        }

        // 4. Check other objects in room (NPCs, items, etc.) by name and aliases
        var contents = _state.Containers.GetContents(roomId);
        foreach (var objId in contents)
        {
            if (objId == playerId) continue;

            var obj = _state.Objects.Get<IMudObject>(objId);
            if (obj is null) continue;

            // Check if name matches
            bool matches = obj.Name.ToLowerInvariant().Contains(normalizedTarget);

            // For IItem, also check aliases and ShortDescription
            if (!matches && obj is IItem itemObj)
            {
                foreach (var alias in itemObj.Aliases)
                {
                    if (alias.ToLowerInvariant().Contains(normalizedTarget) ||
                        normalizedTarget.Contains(alias.ToLowerInvariant()))
                    {
                        matches = true;
                        break;
                    }
                }
                if (!matches && itemObj.ShortDescription.ToLowerInvariant().Contains(normalizedTarget))
                {
                    matches = true;
                }
            }

            if (matches)
            {
                // Check object details first
                foreach (var (keyword, description) in obj.Details)
                {
                    if (keyword.ToLowerInvariant().Contains(normalizedTarget) ||
                        normalizedTarget.Contains(keyword.ToLowerInvariant()))
                    {
                        await session.WriteLineAsync(description);
                        return;
                    }
                }
                // For items, show long description
                if (obj is IItem item)
                {
                    await session.WriteLineAsync(item.LongDescription);
                    return;
                }
                // For livings, show their description and HP
                if (obj is ILiving living)
                {
                    await session.WriteLineAsync(living.Description);
                    await session.WriteLineAsync($"  HP: {living.HP}/{living.MaxHP}");
                    return;
                }
                await session.WriteLineAsync($"You see {obj.Name}.");
                return;
            }
        }

        // 5. Check other players in the room (players are ILiving)
        var allSessions = _state.Sessions.GetAll();
        foreach (var otherSession in allSessions)
        {
            if (otherSession.PlayerId is null || otherSession.PlayerId == playerId)
                continue;

            var otherRoomId = _state.Containers.GetContainer(otherSession.PlayerId);
            if (otherRoomId != roomId)
                continue;

            var otherName = otherSession.PlayerName ?? "someone";
            if (otherName.ToLowerInvariant().Contains(normalizedTarget) ||
                normalizedTarget.Contains(otherName.ToLowerInvariant()))
            {
                // Players are ILiving - display same as other livings
                var otherLiving = _state.Objects!.Get<ILiving>(otherSession.PlayerId);
                if (otherLiving is not null)
                {
                    await session.WriteLineAsync(otherLiving.Description);
                    await session.WriteLineAsync($"  HP: {otherLiving.HP}/{otherLiving.MaxHP}");
                }
                else
                {
                    await session.WriteLineAsync($"You see {otherName} here.");
                }
                return;
            }
        }

        await session.WriteLineAsync($"You don't see '{target}' here.");
    }

    private async Task ExamineItemAsync(ISession session, string itemName)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId is null)
        {
            await session.WriteLineAsync("You're not in a room.");
            return;
        }

        // Find item in room or inventory
        var ctx = CreateContextFor(playerId);
        var itemId = ctx.FindItem(itemName, playerId);  // Check inventory first
        if (itemId is null)
        {
            itemId = ctx.FindItem(itemName, roomId);  // Then check room
        }

        if (itemId is null)
        {
            await session.WriteLineAsync($"You don't see '{itemName}' here.");
            return;
        }

        var item = _state.Objects!.Get<IItem>(itemId);
        if (item is not null)
        {
            await session.WriteLineAsync(item.LongDescription);
            await session.WriteLineAsync($"  Weight: {item.Weight} lbs");
            await session.WriteLineAsync($"  Value: {item.Value} coins");
        }
        else
        {
            var obj = _state.Objects.Get<IMudObject>(itemId);
            if (obj is not null)
            {
                await session.WriteLineAsync($"{obj.Name}");
            }
            else
            {
                await session.WriteLineAsync("You examine it closely but see nothing special.");
            }
        }
    }

    private async Task ReadObjectAsync(ISession session, string target)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId is null)
        {
            await session.WriteLineAsync("You're not in a room.");
            return;
        }

        var targetLower = target.ToLowerInvariant();

        // Check room contents for IReadable objects
        var roomContents = _state.Containers.GetContents(roomId);
        foreach (var objId in roomContents)
        {
            var readable = _state.Objects!.Get<IReadable>(objId);
            if (readable is null) continue;

            // Check name, ReadableLabel, or item aliases
            if (readable.Name.Contains(targetLower, StringComparison.OrdinalIgnoreCase) ||
                readable.ReadableLabel.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync($"You read the {readable.ReadableLabel}:");
                await session.WriteLineAsync("");
                await session.WriteLineAsync(readable.ReadableText);
                return;
            }

            // Check item aliases
            if (readable is IItem item)
            {
                foreach (var alias in item.Aliases)
                {
                    if (alias.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
                    {
                        await session.WriteLineAsync($"You read the {readable.ReadableLabel}:");
                        await session.WriteLineAsync("");
                        await session.WriteLineAsync(readable.ReadableText);
                        return;
                    }
                }
            }
        }

        // Check room's Details dictionary
        var room = _state.Objects!.Get<IRoom>(roomId);
        if (room is IMudObject mudObj && mudObj.Details.Count > 0)
        {
            foreach (var (key, value) in mudObj.Details)
            {
                if (key.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
                {
                    await session.WriteLineAsync($"You read the {key}:");
                    await session.WriteLineAsync(value);
                    return;
                }
            }
        }

        // Check player's inventory
        var inventory = _state.Containers.GetContents(playerId);
        foreach (var objId in inventory)
        {
            var readable = _state.Objects!.Get<IReadable>(objId);
            if (readable is null) continue;

            if (readable.Name.Contains(targetLower, StringComparison.OrdinalIgnoreCase) ||
                readable.ReadableLabel.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync($"You read the {readable.ReadableLabel}:");
                await session.WriteLineAsync("");
                await session.WriteLineAsync(readable.ReadableText);
                return;
            }

            if (readable is IItem item)
            {
                foreach (var alias in item.Aliases)
                {
                    if (alias.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
                    {
                        await session.WriteLineAsync($"You read the {readable.ReadableLabel}:");
                        await session.WriteLineAsync("");
                        await session.WriteLineAsync(readable.ReadableText);
                        return;
                    }
                }
            }
        }

        await session.WriteLineAsync($"You don't see anything called '{target}' that you can read.");
    }

    private async Task EquipItemAsync(ISession session, string itemName)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);

        // Find item in inventory
        var ctx = CreateContextFor(playerId);
        var itemId = ctx.FindItem(itemName, playerId);
        if (itemId is null)
        {
            await session.WriteLineAsync($"You're not carrying '{itemName}'.");
            return;
        }

        var item = _state.Objects!.Get<IEquippable>(itemId);
        if (item is null)
        {
            await session.WriteLineAsync("That can't be equipped.");
            return;
        }

        // Check if something is already equipped in that slot
        var existingItemId = _state.Equipment.GetEquipped(playerId, item.Slot);
        if (existingItemId is not null)
        {
            var existingItem = _state.Objects.Get<IEquippable>(existingItemId);
            if (existingItem is not null)
            {
                // Unequip existing item first
                var existingItemCtx = CreateContextFor(existingItemId);
                existingItem.OnUnequip(playerId, existingItemCtx);
                await session.WriteLineAsync($"You remove {existingItem.ShortDescription}.");
            }
        }

        // Equip the new item
        _state.Equipment.Equip(playerId, item.Slot, itemId);

        // Call OnEquip hook
        var itemCtx = CreateContextFor(itemId);
        item.OnEquip(playerId, itemCtx);

        await session.WriteLineAsync($"You equip {item.ShortDescription} ({item.Slot}).");
        if (roomId is not null)
        {
            BroadcastToRoom(roomId, $"{session.PlayerName} equips {item.ShortDescription}.", session);
        }
    }

    private async Task UnequipSlotAsync(ISession session, string slotName)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);

        // Try to parse slot name
        if (!Enum.TryParse<EquipmentSlot>(slotName, ignoreCase: true, out var slot))
        {
            // Try partial match
            var matchingSlots = Enum.GetValues<EquipmentSlot>()
                .Where(s => s.ToString().StartsWith(slotName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingSlots.Count == 1)
            {
                slot = matchingSlots[0];
            }
            else if (matchingSlots.Count > 1)
            {
                await session.WriteLineAsync($"Ambiguous slot '{slotName}'. Did you mean: {string.Join(", ", matchingSlots)}?");
                return;
            }
            else
            {
                await session.WriteLineAsync($"Unknown slot '{slotName}'. Valid slots: {string.Join(", ", Enum.GetNames<EquipmentSlot>())}");
                return;
            }
        }

        // Check if something is equipped in that slot
        var itemId = _state.Equipment.GetEquipped(playerId, slot);
        if (itemId is null)
        {
            await session.WriteLineAsync($"Nothing is equipped in {slot}.");
            return;
        }

        var item = _state.Objects!.Get<IEquippable>(itemId);
        if (item is not null)
        {
            // Call OnUnequip hook
            var itemCtx = CreateContextFor(itemId);
            item.OnUnequip(playerId, itemCtx);
        }

        // Unequip the item
        _state.Equipment.Unequip(playerId, slot);

        await session.WriteLineAsync($"You unequip {item?.ShortDescription ?? itemId}.");
        if (roomId is not null)
        {
            BroadcastToRoom(roomId, $"{session.PlayerName} unequips {item?.ShortDescription ?? "something"}.", session);
        }
    }

    private async Task ShowEquipmentAsync(ISession session)
    {
        var playerId = session.PlayerId;
        var fmt = session.Formatter;

        if (playerId is null)
        {
            await session.WriteLineAsync(fmt.FormatError("No player."));
            return;
        }

        var equipped = _state.Equipment.GetAllEquipped(playerId);
        if (equipped.Count == 0)
        {
            await session.WriteLineAsync(fmt.FormatEquipmentEmpty());
            return;
        }

        await session.WriteLineAsync(fmt.FormatEquipmentHeader());
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            if (equipped.TryGetValue(slot, out var itemId))
            {
                var item = _state.Objects!.Get<IItem>(itemId);
                var desc = item?.ShortDescription ?? itemId;
                string? stats = null;

                // Add extra info for weapons/armor
                if (item is IWeapon weapon)
                {
                    stats = $"({weapon.MinDamage}-{weapon.MaxDamage} dmg)";
                }
                else if (item is IArmor armor)
                {
                    stats = $"({armor.ArmorClass} AC)";
                }

                await session.WriteLineAsync(fmt.FormatEquipmentSlot(slot.ToString(), desc, stats));
            }
        }

        // Show totals
        int totalAC = 0;
        int minDmg = 0, maxDmg = 0;
        foreach (var kvp in equipped)
        {
            var item = _state.Objects!.Get<IItem>(kvp.Value);
            if (item is IArmor armor)
            {
                totalAC += armor.ArmorClass;
            }
            if (item is IWeapon weapon)
            {
                minDmg += weapon.MinDamage;
                maxDmg += weapon.MaxDamage;
            }
        }

        if (totalAC > 0 || maxDmg > 0)
        {
            await session.WriteLineAsync(fmt.FormatEquipmentTotals(totalAC, minDmg, maxDmg));
        }
    }

    private void ProcessCombat()
    {
        void SendMessage(string targetId, string message)
        {
            var targetSession = _state.Sessions.GetByPlayerId(targetId);
            if (targetSession is not null)
            {
                _ = targetSession.WriteLineAsync(message);
            }
        }

        var deaths = _state.Combat.ProcessCombatRounds(_state, _clock, SendMessage);

        // Handle deaths - award experience, trigger hooks
        foreach (var (killerId, victimId) in deaths)
        {
            // Get victim for experience value
            var victim = _state.Objects!.Get<ILiving>(victimId);
            var victimName = victim?.Name ?? victimId;

            // Notify the room about the death
            var victimRoom = _state.Containers.GetContainer(victimId);
            if (victimRoom is not null)
            {
                BroadcastToRoom(victimRoom, $"{victimName} has been slain!");
            }

            // Award experience if killer is a player
            var killerSession = _state.Sessions.GetByPlayerId(killerId);
            if (killerSession is not null)
            {
                var player = _state.Objects.Get<IPlayer>(killerId);
                if (player is not null)
                {
                    // Base XP is victim's MaxHP
                    int xpValue = victim?.MaxHP ?? 10;
                    var ctx = CreateContextFor(killerId);
                    player.AwardExperience(xpValue, ctx);
                    _ = killerSession.WriteLineAsync($"You gain {xpValue} experience points!");
                }

                // Call OnKill hook
                var killerObj = _state.Objects.Get<IMudObject>(killerId);
                if (killerObj is IOnKill onKill)
                {
                    var ctx = CreateContextFor(killerId);
                    SafeInvoker.TryInvokeHook(() => onKill.OnKill(victimId, ctx), $"OnKill in {killerId}");
                }
            }
        }
    }

    private async Task StartCombatAsync(ISession session, string targetName)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        // Check if already in combat
        if (_state.Combat.IsInCombat(playerId))
        {
            var currentTarget = _state.Combat.GetCombatTarget(playerId);
            var currentTargetObj = _state.Objects!.Get<IMudObject>(currentTarget!);
            await session.WriteLineAsync($"You are already fighting {currentTargetObj?.Name ?? currentTarget}!");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId is null)
        {
            await session.WriteLineAsync("You're not in a room.");
            return;
        }

        // Find target in room
        var targetId = FindTargetInRoom(targetName, roomId, playerId);
        if (targetId is null)
        {
            await session.WriteLineAsync($"You don't see '{targetName}' here.");
            return;
        }

        // Can't attack yourself
        if (targetId == playerId)
        {
            await session.WriteLineAsync("You can't attack yourself!");
            return;
        }

        // Check if target is a living thing
        var target = _state.Objects!.Get<ILiving>(targetId);
        if (target is null)
        {
            await session.WriteLineAsync("You can't attack that.");
            return;
        }

        if (!target.IsAlive)
        {
            await session.WriteLineAsync($"{target.Name} is already dead.");
            return;
        }

        // Start combat
        _state.Combat.StartCombat(playerId, targetId, _clock.Now);

        await session.WriteLineAsync($"You attack {target.Name}!");
        BroadcastToRoom(roomId, $"{session.PlayerName} attacks {target.Name}!", session);
        _state.EventLog.Record(roomId, $"{session.PlayerName} attacked {target.Name}");

        // If target is not already in combat, they fight back
        if (!_state.Combat.IsInCombat(targetId))
        {
            _state.Combat.StartCombat(targetId, playerId, _clock.Now);
        }
    }

    private async Task AttemptFleeAsync(ISession session)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        if (!_state.Combat.IsInCombat(playerId))
        {
            await session.WriteLineAsync("You're not in combat.");
            return;
        }

        var exitDir = _state.Combat.AttemptFlee(playerId, _state, _clock);

        if (exitDir is null)
        {
            await session.WriteLineAsync("You fail to escape!");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);
        BroadcastToRoom(roomId!, $"{session.PlayerName} flees {exitDir}!", session);

        await session.WriteLineAsync($"You flee to the {exitDir}!");

        // Actually move the player
        await GoAsync(session, exitDir);
    }

    private async Task ConsiderTargetAsync(ISession session, string targetName)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId is null)
        {
            await session.WriteLineAsync("You're not in a room.");
            return;
        }

        // Find target in room
        var targetId = FindTargetInRoom(targetName, roomId, playerId);
        if (targetId is null)
        {
            await session.WriteLineAsync($"You don't see '{targetName}' here.");
            return;
        }

        var target = _state.Objects!.Get<ILiving>(targetId);
        if (target is null)
        {
            await session.WriteLineAsync("You can't fight that.");
            return;
        }

        var player = _state.Objects.Get<ILiving>(playerId);
        if (player is null)
        {
            await session.WriteLineAsync("Error getting player stats.");
            return;
        }

        // Compare levels/HP
        var playerPower = player.MaxHP;
        var targetPower = target.MaxHP;

        string difficulty;
        if (targetPower < playerPower * 0.5)
            difficulty = "an easy target";
        else if (targetPower < playerPower * 0.8)
            difficulty = "a fair fight";
        else if (targetPower < playerPower * 1.2)
            difficulty = "a challenging opponent";
        else if (targetPower < playerPower * 2.0)
            difficulty = "a dangerous foe";
        else
            difficulty = "certain death";

        await session.WriteLineAsync($"{target.Name} looks like {difficulty}.");
        await session.WriteLineAsync($"  HP: {target.HP}/{target.MaxHP}");

        if (target is IHasEquipment equipped)
        {
            await session.WriteLineAsync($"  Armor Class: {equipped.TotalArmorClass}");
            var (min, max) = equipped.WeaponDamage;
            if (max > 0)
            {
                await session.WriteLineAsync($"  Weapon Damage: {min}-{max}");
            }
        }
    }

    private async Task TriggerNpcRoomEventAsync(RoomEvent roomEvent, string roomId)
    {
        if (_state.Objects is null) return;

        var contents = _state.Containers.GetContents(roomId);
        foreach (var objId in contents)
        {
            // Skip the actor - they don't need to react to their own actions
            if (objId == roomEvent.ActorId) continue;

            var obj = _state.Objects.Get<IMudObject>(objId);
            if (obj is ILlmNpc llmNpc)
            {
                try
                {
                    var ctx = CreateContextFor(objId);
                    await llmNpc.OnRoomEventAsync(roomEvent, ctx);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LLM] Error in NPC {objId} response: {ex.Message}");
                }
            }
        }
    }

    private static string? GetOppositeDirection(string direction)
    {
        return direction.ToLowerInvariant() switch
        {
            "north" or "n" => "south",
            "south" or "s" => "north",
            "east" or "e" => "west",
            "west" or "w" => "east",
            "up" or "u" => "below",
            "down" or "d" => "above",
            "northeast" or "ne" => "southwest",
            "northwest" or "nw" => "southeast",
            "southeast" or "se" => "northwest",
            "southwest" or "sw" => "northeast",
            _ => null
        };
    }

    private string? FindTargetInRoom(string name, string roomId, string? excludeId = null)
    {
        if (_state.Objects is null)
            return null;

        var normalizedName = name.ToLowerInvariant();
        var contents = _state.Containers.GetContents(roomId);

        foreach (var objId in contents)
        {
            if (objId == excludeId)
                continue;  // Skip self

            var obj = _state.Objects.Get<IMudObject>(objId);
            if (obj is null)
                continue;

            // Check if object name contains the search term (case-insensitive)
            if (obj.Name.ToLowerInvariant().Contains(normalizedName))
                return objId;

            // Also check player name for other players
            if (obj is IPlayer)
            {
                var otherSession = _state.Sessions.GetByPlayerId(objId);
                if (otherSession?.PlayerName?.ToLowerInvariant().Contains(normalizedName) == true)
                    return objId;
            }
        }

        return null;
    }

    private async Task SavePlayerDataAsync(ISession session, string playerId)
    {
        var playerName = session.PlayerName;
        if (playerName is null) return;

        try
        {
            // Load existing account data
            var accountData = await _accounts.LoadPlayerDataAsync(playerName);
            if (accountData is null) return;

            // Update location
            accountData.Location = _state.Containers.GetContainer(playerId);

            // Update state from IStateStore
            var stateStore = _state.Objects!.GetStateStore(playerId);
            if (stateStore is not null)
            {
                accountData.State.Clear();
                foreach (var key in stateStore.Keys)
                {
                    var value = stateStore.Get<object>(key);
                    if (value is not null)
                    {
                        // Convert to JsonElement for storage
                        var json = JsonSerializer.Serialize(value);
                        accountData.State[key] = JsonSerializer.Deserialize<JsonElement>(json);
                    }
                }
            }

            // Update inventory
            accountData.Inventory.Clear();
            var inventory = _state.Containers.GetContents(playerId);
            foreach (var itemId in inventory)
            {
                // Skip equipped items - they'll be saved in equipment
                var isEquipped = _state.Equipment.GetAllEquipped(playerId).Values.Contains(itemId);
                accountData.Inventory.Add(itemId);
            }

            // Update equipment
            accountData.Equipment.Clear();
            var equipped = _state.Equipment.GetAllEquipped(playerId);
            foreach (var (slot, itemId) in equipped)
            {
                accountData.Equipment[slot.ToString()] = itemId;
            }

            // Save to file
            await _accounts.SavePlayerDataAsync(playerName, accountData);
            Console.WriteLine($"[{session.SessionId}] Saved player data for {playerName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{session.SessionId}] Error saving player data: {ex.Message}");
        }
    }

    private void CleanupPlayerItems(string playerId)
    {
        // Get all items in player's inventory
        var inventory = _state.Containers.GetContents(playerId).ToList();

        // Remove equipment first
        var equipped = _state.Equipment.GetAllEquipped(playerId);
        foreach (var slot in equipped.Keys.ToList())
        {
            _state.Equipment.Unequip(playerId, slot);
        }

        // Destruct all inventory items (they're saved to player file)
        foreach (var itemId in inventory)
        {
            _state.Containers.Remove(itemId);
            // Don't destruct - just remove from containers
            // Items will be re-created when player logs in
        }
    }

    private async Task ShowStatAsync(ISession session, string objectId)
    {
        // Try to find as instance first
        var instance = _state.Objects!.Get<IMudObject>(objectId);
        if (instance is not null)
        {
            await session.WriteLineAsync($"=== Instance: {objectId} ===");
            await session.WriteLineAsync($"  Name: {instance.Name}");
            await session.WriteLineAsync($"  Type: {instance.GetType().Name}");

            var stateStore = _state.Objects.GetStateStore(objectId);
            if (stateStore is not null && stateStore.Keys.Any())
            {
                await session.WriteLineAsync("  State:");
                foreach (var key in stateStore.Keys)
                {
                    var value = stateStore.Get<object>(key);
                    await session.WriteLineAsync($"    {key}: {value}");
                }
            }

            var container = _state.Containers.GetContainer(objectId);
            if (container is not null)
            {
                await session.WriteLineAsync($"  Container: {container}");
            }
            return;
        }

        // Try as blueprint
        if (_state.Objects.ListBlueprintIds().Contains(objectId))
        {
            await session.WriteLineAsync($"=== Blueprint: {objectId} ===");
            var instances = _state.Objects.ListInstanceIds()
                .Where(id => id.StartsWith(objectId + "#"))
                .ToList();
            await session.WriteLineAsync($"  Instances: {instances.Count}");
            foreach (var id in instances.Take(10))
            {
                await session.WriteLineAsync($"    {id}");
            }
            if (instances.Count > 10)
            {
                await session.WriteLineAsync($"    ... and {instances.Count - 10} more");
            }
            return;
        }

        await session.WriteLineAsync($"Object '{objectId}' not found.");
    }

    private async Task ResetObjectAsync(ISession session, string objectId)
    {
        var obj = _state.Objects!.Get<IMudObject>(objectId);
        if (obj is not IResettable resettable)
        {
            await session.WriteLineAsync($"Object '{objectId}' not found or not resettable.");
            return;
        }

        var ctx = CreateContextFor(objectId);
        resettable.Reset(ctx);
        await session.WriteLineAsync($"Reset {objectId}.");
    }

    private async Task HandlePatchAsync(ISession session, string[] parts)
    {
        if (parts.Length < 2)
        {
            await session.WriteLineAsync("Usage: patch <objectId> [key] [value]");
            return;
        }

        var objectId = parts[1];
        var stateStore = _state.Objects!.GetStateStore(objectId);
        if (stateStore is null)
        {
            await session.WriteLineAsync($"Object '{objectId}' not found or has no state.");
            return;
        }

        // Just show state if no key provided
        if (parts.Length == 2)
        {
            await session.WriteLineAsync($"=== State for {objectId} ===");
            foreach (var key in stateStore.Keys)
            {
                var value = stateStore.Get<object>(key);
                await session.WriteLineAsync($"  {key}: {value}");
            }
            return;
        }

        var stateKey = parts[2];

        // Show specific key if no value provided
        if (parts.Length == 3)
        {
            var value = stateStore.Get<object>(stateKey);
            await session.WriteLineAsync($"{stateKey}: {value ?? "(null)"}");
            return;
        }

        // Set value
        var newValue = string.Join(" ", parts.Skip(3));

        // Try to parse as number
        if (int.TryParse(newValue, out var intVal))
        {
            stateStore.Set(stateKey, intVal);
        }
        else if (double.TryParse(newValue, out var dblVal))
        {
            stateStore.Set(stateKey, dblVal);
        }
        else if (bool.TryParse(newValue, out var boolVal))
        {
            stateStore.Set(stateKey, boolVal);
        }
        else
        {
            stateStore.Set(stateKey, newValue);
        }

        await session.WriteLineAsync($"Set {stateKey} = {newValue}");
    }

    private async Task HandleLsAsync(ISession session, string[] args)
    {
        var worldRoot = _state.Objects?.WorldRoot ?? "World";
        worldRoot = Path.GetFullPath(worldRoot);
        var sessionId = session.SessionId;

        // Determine target path
        var targetVirtualPath = args.Length > 0
            ? Commands.Wizard.WizardFilesystem.ResolvePath(sessionId, string.Join(" ", args), worldRoot)
            : Commands.Wizard.WizardFilesystem.GetWorkingDir(sessionId);

        if (targetVirtualPath is null)
        {
            await session.WriteLineAsync("Invalid path.");
            return;
        }

        var targetFsPath = Commands.Wizard.WizardFilesystem.ToFilesystemPath(targetVirtualPath, worldRoot);

        if (!Directory.Exists(targetFsPath))
        {
            if (File.Exists(targetFsPath))
            {
                await session.WriteLineAsync(Path.GetFileName(targetFsPath));
                return;
            }
            await session.WriteLineAsync($"Directory not found: {targetVirtualPath}");
            return;
        }

        await session.WriteLineAsync($"Contents of {targetVirtualPath}:");

        // List directories first
        var dirs = Directory.GetDirectories(targetFsPath)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .OrderBy(n => n);

        foreach (var dir in dirs)
        {
            await session.WriteLineAsync($"  {dir}/");
        }

        // Then list files
        var files = Directory.GetFiles(targetFsPath)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .OrderBy(n => n);

        foreach (var file in files)
        {
            await session.WriteLineAsync($"  {file}");
        }

        if (!dirs.Any() && !files.Any())
        {
            await session.WriteLineAsync("  (empty)");
        }
    }

    private async Task HandleCdAsync(ISession session, string[] args)
    {
        var worldRoot = _state.Objects?.WorldRoot ?? "World";
        worldRoot = Path.GetFullPath(worldRoot);
        var sessionId = session.SessionId;

        if (args.Length == 0)
        {
            Commands.Wizard.WizardFilesystem.SetWorkingDir(sessionId, "/");
            await session.WriteLineAsync("/");
            return;
        }

        var targetPath = string.Join(" ", args);
        var resolvedPath = Commands.Wizard.WizardFilesystem.ResolvePath(sessionId, targetPath, worldRoot);

        if (resolvedPath is null)
        {
            await session.WriteLineAsync("Invalid path - cannot leave World directory.");
            return;
        }

        var fsPath = Commands.Wizard.WizardFilesystem.ToFilesystemPath(resolvedPath, worldRoot);

        if (!Directory.Exists(fsPath))
        {
            await session.WriteLineAsync($"Directory not found: {resolvedPath}");
            return;
        }

        Commands.Wizard.WizardFilesystem.SetWorkingDir(sessionId, resolvedPath);
        await session.WriteLineAsync(resolvedPath);
    }

    private async Task HandleCatAsync(ISession session, string[] args)
    {
        if (args.Length == 0)
        {
            await session.WriteLineAsync("Usage: cat <file>");
            return;
        }

        var worldRoot = _state.Objects?.WorldRoot ?? "World";
        worldRoot = Path.GetFullPath(worldRoot);
        var sessionId = session.SessionId;

        var filePath = string.Join(" ", args);
        var resolvedPath = Commands.Wizard.WizardFilesystem.ResolvePath(sessionId, filePath, worldRoot);

        if (resolvedPath is null)
        {
            await session.WriteLineAsync("Invalid path.");
            return;
        }

        var fsPath = Commands.Wizard.WizardFilesystem.ToFilesystemPath(resolvedPath, worldRoot);

        if (!File.Exists(fsPath))
        {
            await session.WriteLineAsync($"File not found: {resolvedPath}");
            return;
        }

        var lines = await File.ReadAllLinesAsync(fsPath);
        await session.WriteLineAsync($"=== {resolvedPath} ({lines.Length} lines) ===");

        var lineNum = 1;
        foreach (var line in lines)
        {
            await session.WriteLineAsync($"{lineNum,4}: {line}");
            lineNum++;
        }

        await session.WriteLineAsync($"=== End of {resolvedPath} ===");
    }

    private async Task HandleMoreAsync(ISession session, string[] args)
    {
        if (args.Length == 0)
        {
            await session.WriteLineAsync("Usage: more <file> [start_line] [num_lines]");
            await session.WriteLineAsync("  start_line: Line to start from (default: 1)");
            await session.WriteLineAsync("  num_lines: Number of lines to show (default: 20)");
            return;
        }

        var worldRoot = _state.Objects?.WorldRoot ?? "World";
        worldRoot = Path.GetFullPath(worldRoot);
        var sessionId = session.SessionId;

        var filePath = args[0];
        var startLine = 1;
        var numLines = 20;

        if (args.Length >= 2 && int.TryParse(args[1], out var parsedStart))
        {
            startLine = Math.Max(1, parsedStart);
        }

        if (args.Length >= 3 && int.TryParse(args[2], out var parsedNum))
        {
            numLines = Math.Max(1, Math.Min(100, parsedNum));
        }

        var resolvedPath = Commands.Wizard.WizardFilesystem.ResolvePath(sessionId, filePath, worldRoot);

        if (resolvedPath is null)
        {
            await session.WriteLineAsync("Invalid path.");
            return;
        }

        var fsPath = Commands.Wizard.WizardFilesystem.ToFilesystemPath(resolvedPath, worldRoot);

        if (!File.Exists(fsPath))
        {
            await session.WriteLineAsync($"File not found: {resolvedPath}");
            return;
        }

        var lines = await File.ReadAllLinesAsync(fsPath);
        var totalLines = lines.Length;
        var endLine = Math.Min(startLine + numLines - 1, totalLines);

        await session.WriteLineAsync($"=== {resolvedPath} (lines {startLine}-{endLine} of {totalLines}) ===");

        for (var i = startLine - 1; i < endLine && i < lines.Length; i++)
        {
            await session.WriteLineAsync($"{i + 1,4}: {lines[i]}");
        }

        if (endLine < totalLines)
        {
            await session.WriteLineAsync($"=== More: 'more {filePath} {endLine + 1}' for next page ===");
        }
        else
        {
            await session.WriteLineAsync($"=== End of {resolvedPath} ===");
        }
    }

    private async Task HandleEditAsync(ISession session, string[] args)
    {
        if (args.Length == 0)
        {
            await session.WriteLineAsync("Usage: edit <file>");
            return;
        }

        if (!session.SupportsAnsi)
        {
            await session.WriteLineAsync("The editor requires ANSI terminal support.");
            return;
        }

        var worldRoot = _state.Objects?.WorldRoot ?? "World";
        worldRoot = Path.GetFullPath(worldRoot);
        var sessionId = session.SessionId;

        var filePath = string.Join(" ", args);
        var resolvedPath = Commands.Wizard.WizardFilesystem.ResolvePath(sessionId, filePath, worldRoot);

        if (resolvedPath is null)
        {
            await session.WriteLineAsync("Invalid path.");
            return;
        }

        var fsPath = Commands.Wizard.WizardFilesystem.ToFilesystemPath(resolvedPath, worldRoot);

        // Load existing content or start with empty file
        string[] content;
        if (File.Exists(fsPath))
        {
            content = await File.ReadAllLinesAsync(fsPath);
        }
        else
        {
            var dir = Path.GetDirectoryName(fsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                await session.WriteLineAsync($"Directory does not exist: {Path.GetDirectoryName(resolvedPath)}");
                return;
            }
            content = Array.Empty<string>();
        }

        await session.WriteLineAsync($"Opening {resolvedPath}...");

        var editor = new Commands.Wizard.TextEditor(session, fsPath, resolvedPath, content);
        Func<Task<char?>> readChar = () => session.ReadCharAsync();

        var saved = await editor.RunAsync(readChar, _cancellationToken);

        if (saved)
        {
            await session.WriteLineAsync($"Saved {resolvedPath}");
        }
        else
        {
            await session.WriteLineAsync("Editor closed.");
        }
    }

    private async Task ToggleColorsAsync(ISession session, string? argument)
    {
        var fmt = session.Formatter;

        if (argument is null)
        {
            // Show current status
            var status = session.SupportsAnsi ? "enabled" : "disabled";
            await session.WriteLineAsync(fmt.FormatInfo($"Colors are currently {status}. Use 'colors on' or 'colors off' to change."));
            return;
        }

        switch (argument.ToLowerInvariant())
        {
            case "on":
            case "true":
            case "yes":
            case "enable":
                session.SupportsAnsi = true;
                await session.WriteLineAsync(session.Formatter.FormatSuccess("Colors enabled."));
                break;

            case "off":
            case "false":
            case "no":
            case "disable":
                session.SupportsAnsi = false;
                await session.WriteLineAsync(session.Formatter.FormatInfo("Colors disabled."));
                break;

            case "test":
                // Output raw ANSI escape codes directly to test terminal support
                await session.WriteLineAsync("Testing ANSI color support...");
                await session.WriteLineAsync("");

                // Raw ANSI codes (ESC = \x1b = \u001b)
                await session.WriteLineAsync("Raw ANSI escape codes:");
                await session.WriteLineAsync("\u001b[31mThis should be RED\u001b[0m");
                await session.WriteLineAsync("\u001b[32mThis should be GREEN\u001b[0m");
                await session.WriteLineAsync("\u001b[33mThis should be YELLOW\u001b[0m");
                await session.WriteLineAsync("\u001b[34mThis should be BLUE\u001b[0m");
                await session.WriteLineAsync("\u001b[1;35mThis should be BOLD MAGENTA\u001b[0m");
                await session.WriteLineAsync("");

                // Spectre.Console generated (real Spectre rendering to session output)
                await session.WriteLineAsync("Spectre.Console generated (table/panel):");
                var spectre = SpectreSessionRenderer.Render(console =>
                {
                    console.MarkupLine("[bold yellow]Spectre markup[/] [green]works[/] if ANSI is enabled.");
                    var panel = new Panel("This is a [blue]Panel[/].")
                        .Header("Spectre.Console");
                    // Full-featured Spectre output (Unicode box drawing).
                    panel.Border = BoxBorder.Rounded;
                    console.Write(panel);

                    var table = new Table();
                    table.AddColumn("Key");
                    table.AddColumn("Value");
                    table.Border(TableBorder.Rounded);
                    table.AddRow("SupportsAnsi", session.SupportsAnsi.ToString());
                    table.AddRow("Formatter", session.Formatter.GetType().Name);
                    table.AddRow("SessionId", session.SessionId);
                    console.Write(table);
                }, new SpectreRenderOptions(
                    EnableAnsi: session.SupportsAnsi,
                    EnableUnicode: true,
                    Width: session.TerminalSize.Width,
                    Height: session.TerminalSize.Height,
                    ColorSystem: ColorSystemSupport.Standard));

                await session.WriteAsync(spectre);
                await session.WriteLineAsync("");

                // Show formatter type
                await session.WriteLineAsync($"Current formatter: {session.Formatter.GetType().Name}");
                await session.WriteLineAsync($"SupportsAnsi: {session.SupportsAnsi}");
                break;

            default:
                await session.WriteLineAsync(fmt.FormatError("Usage: colors on|off|test"));
                break;
        }
    }
}
