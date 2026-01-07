using System.Text.Json;
using System.Diagnostics;
using JitRealm.Mud.AI;
using JitRealm.Mud.Commands;
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
    private readonly CommandRegistry _commandRegistry;
    private readonly TelnetCommandDispatcher _commandDispatcher;
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
        _commandRegistry = CommandFactory.CreateRegistry();
        _commandDispatcher = new TelnetCommandDispatcher(state, _commandRegistry, CreateContextFor);
        _telnet = new TelnetServer(settings.Server.Port);

        _telnet.OnClientConnected += OnClientConnected;

        // Register immediate message delivery for async LLM responses
        _state.Messages.ImmediateDeliveryHandler = DeliverMessageImmediately;
    }

    /// <summary>
    /// Deliver a message immediately when enqueued (used for async LLM responses).
    /// Returns true if delivered (skip queue), false to queue for later.
    /// </summary>
    private bool DeliverMessageImmediately(MudMessage msg)
    {
        var delivered = false;

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
                    delivered = true;
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
                        // Don't echo room messages back to the sender
                        if (session.PlayerId == msg.FromId) continue;
                        _ = session.WriteLineAsync(formatted);
                        delivered = true;
                    }
                }
                break;
        }

        return delivered;
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

        // Display MOTD if exists
        await DisplayMotdAsync(session);

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

    /// <summary>
    /// Display the Message of the Day if it exists.
    /// </summary>
    private async Task DisplayMotdAsync(ISession session)
    {
        var motdPath = Path.Combine(AppContext.BaseDirectory, _settings.Paths.WorldDirectory, "motd.txt");
        if (File.Exists(motdPath))
        {
            await session.WriteLineAsync("");
            await session.WriteLineAsync(File.ReadAllText(motdPath));
        }
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

        // Try the unified command registry first
        if (await _commandDispatcher.TryExecuteAsync(session, cmd, parts.Skip(1).ToArray()))
        {
            // Command handled by registry - deliver any messages
            DeliverMessages();
            return;
        }

        // Telnet-specific commands not in the registry
        switch (cmd)
        {
            // Persistence commands - require access to _persistence
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
            else if (obj is LivingBase living)
            {
                // Non-player living: show short room-facing description ("a shopkeeper")
                objectNames.Add(living.ShortDescription);
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
                            // Don't echo room messages back to the sender
                            if (session.PlayerId == msg.FromId) continue;
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
        if (obj is LivingBase living && obj is not IPlayer)
            return living.ShortDescription;

        return obj?.Name ?? objectId;
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

            // Update inventory (save blueprint IDs, not instance IDs)
            accountData.Inventory.Clear();
            var inventory = _state.Containers.GetContents(playerId);
            foreach (var itemId in inventory)
            {
                // Extract blueprint ID from instance ID (e.g., "Items/sword.cs#000001" -> "Items/sword.cs")
                var blueprintId = itemId.Contains('#') ? itemId.Split('#')[0] : itemId;
                accountData.Inventory.Add(blueprintId);
            }

            // Update equipment (save blueprint IDs, not instance IDs)
            accountData.Equipment.Clear();
            var equipped = _state.Equipment.GetAllEquipped(playerId);
            foreach (var (slot, itemId) in equipped)
            {
                // Extract blueprint ID from instance ID
                var blueprintId = itemId.Contains('#') ? itemId.Split('#')[0] : itemId;
                accountData.Equipment[slot.ToString()] = blueprintId;
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
}
