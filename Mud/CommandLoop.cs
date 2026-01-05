using System.Text.Json;
using JitRealm.Mud.Commands;
using JitRealm.Mud.Configuration;
using JitRealm.Mud.Network;
using JitRealm.Mud.Persistence;
using JitRealm.Mud.Players;
using JitRealm.Mud.Security;

namespace JitRealm.Mud;

public sealed class CommandLoop
{
    private readonly WorldState _state;
    private readonly WorldStatePersistence _persistence;
    private readonly DriverSettings _settings;
    private readonly IClock _clock;
    private readonly PlayerAccountService _accounts;
    private string? _playerId;
    private readonly ConsoleSession _session;
    private readonly CommandRegistry _commandRegistry;

    public CommandLoop(WorldState state, WorldStatePersistence persistence, DriverSettings settings)
    {
        _state = state;
        _persistence = persistence;
        _settings = settings;
        _clock = state.Clock;
        _accounts = new PlayerAccountService(settings);
        _session = new ConsoleSession();
        _commandRegistry = CommandFactory.CreateRegistry();
    }

    public async Task RunAsync()
    {
        Console.WriteLine($"{_settings.Server.MudName} v{_settings.Server.Version}");
        Console.WriteLine();

        // Login or register
        var loggedIn = await LoginOrRegisterAsync();
        if (!loggedIn)
        {
            Console.WriteLine("Goodbye!");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Commands: look, go <exit>, get <item>, drop <item>, inventory, examine <item>,");
        Console.WriteLine("          equip <item>, unequip <slot>, equipment, score,");
        Console.WriteLine("          kill <target>, flee, consider <target>,");
        Console.WriteLine("          shout <msg>, whisper <player> <msg>, who, help [cmd],");
        Console.WriteLine("          objects, blueprints, clone <bp>, destruct <id>, stat <id>,");
        Console.WriteLine("          reload <bp>, unload <bp>, reset <id>, save, load, quit");

        while (true)
        {
            // Process any due heartbeats
            ProcessHeartbeats();

            // Process any due callouts
            ProcessCallOuts();

            // Process any combat rounds
            ProcessCombat();

            // Display any pending messages
            DisplayMessages();

            Console.Write("> ");
            var input = Console.ReadLine();
            if (input is null)
            {
                // When stdin is closed/redirected (e.g. background run), ReadLine() can return null immediately,
                // which would otherwise cause a tight loop printing prompts forever.
                Console.WriteLine();
                Console.WriteLine("Input closed. Exiting.");
                return;
            }
            if (string.IsNullOrWhiteSpace(input))
                continue;

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();

            try
            {
                // Special handling for quit/exit to terminate the game loop
                if (cmd is "quit" or "exit")
                {
                    await LogoutPlayerAsync();
                    return;
                }

                // Console-only commands that need _persistence
                if (cmd == "save" && _session.IsWizard)
                {
                    await _persistence.SaveAsync(_state, _session);
                    Console.WriteLine("World state saved.");
                    continue;
                }
                if (cmd == "load" && _session.IsWizard)
                {
                    var loaded = await _persistence.LoadAsync(_state, _session);
                    if (loaded)
                    {
                        _playerId = _session.PlayerId;
                        Console.WriteLine("World state loaded.");
                        await TryExecuteRegisteredCommandAsync("look", Array.Empty<string>());
                    }
                    else
                    {
                        Console.WriteLine("No saved state found.");
                    }
                    continue;
                }

                // All other commands are handled through the registry
                if (!await TryExecuteRegisteredCommandAsync(cmd, parts.Skip(1).ToArray()))
                {
                    Console.WriteLine("Unknown command. Type 'help' for a list of commands.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private async Task<bool> LoginOrRegisterAsync()
    {
        Console.WriteLine("1. Login");
        Console.WriteLine("2. Create new player");
        Console.Write("Choice (1/2): ");

        var choice = Console.ReadLine()?.Trim();
        if (choice == "2")
        {
            return await HandleRegistrationAsync();
        }
        else
        {
            return await HandleLoginAsync();
        }
    }

    private async Task<bool> HandleLoginAsync()
    {
        Console.WriteLine();

        // Get player name
        Console.Write("Enter player name: ");
        var name = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!await _accounts.PlayerExistsAsync(name))
        {
            Console.WriteLine("Player not found.");
            return false;
        }

        // Get password
        Console.Write("Enter password: ");
        var password = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(password))
            return false;

        if (!await _accounts.ValidateCredentialsAsync(name, password))
        {
            Console.WriteLine("Invalid password.");
            return false;
        }

        // Load player data
        var accountData = await _accounts.LoadPlayerDataAsync(name);
        if (accountData is null)
        {
            Console.WriteLine("Error loading player data.");
            return false;
        }

        // Update last login time
        await _accounts.UpdateLastLoginAsync(name);

        // Create player in world
        await SetupPlayerInWorldAsync(accountData);
        return true;
    }

    private async Task<bool> HandleRegistrationAsync()
    {
        Console.WriteLine();

        // Get player name
        Console.Write("Enter player name: ");
        var name = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var nameError = PlayerAccountService.ValidatePlayerName(name);
        if (nameError is not null)
        {
            Console.WriteLine(nameError);
            return false;
        }

        if (await _accounts.PlayerExistsAsync(name))
        {
            Console.WriteLine("That name is already taken.");
            return false;
        }

        // Get password
        Console.Write("Enter password: ");
        var password = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(password))
            return false;

        var passwordError = PlayerAccountService.ValidatePassword(password);
        if (passwordError is not null)
        {
            Console.WriteLine(passwordError);
            return false;
        }

        Console.Write("Confirm password: ");
        var confirm = Console.ReadLine();
        if (confirm != password)
        {
            Console.WriteLine("Passwords don't match.");
            return false;
        }

        // Create account
        var accountData = await _accounts.CreateAccountAsync(name, password);

        Console.WriteLine();
        Console.WriteLine($"Welcome to the realm, {name}!");

        // Create player in world
        await SetupPlayerInWorldAsync(accountData);
        return true;
    }

    private async Task SetupPlayerInWorldAsync(PlayerAccountData accountData)
    {
        var playerName = accountData.Name;

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

        // Process spawns for the start room
        await _state.ProcessSpawnsAsync(startRoom.Id, _clock);

        // Clone a player from the player blueprint
        var player = await _state.Objects.CloneAsync<IPlayer>(_settings.Paths.PlayerBlueprint, _state);
        _playerId = player.Id;

        // Set up the session
        _session.PlayerId = _playerId;
        _session.PlayerName = playerName;
        _session.IsWizard = accountData.IsWizard;
        _state.Sessions.Add(_session);

        // Create context and set player name
        var ctx = CreateContextFor(_playerId);
        if (player is PlayerBase playerBase)
        {
            playerBase.SetPlayerName(playerName, ctx);
        }

        // Restore saved state if any
        if (accountData.State is not null && accountData.State.Count > 0)
        {
            var stateStore = _state.Objects.GetStateStore(_playerId);
            if (stateStore is not null)
            {
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
        }

        // Move player to start room
        _state.Containers.Add(startRoom.Id, _playerId);

        // Restore inventory
        if (accountData.Inventory is not null)
        {
            foreach (var savedId in accountData.Inventory)
            {
                try
                {
                    // Handle both old instance IDs and new blueprint IDs
                    var blueprintId = savedId.Contains('#') ? savedId.Split('#')[0] : savedId;
                    var item = await _state.Objects.CloneAsync<IItem>(blueprintId, _state);
                    _state.Containers.Add(_playerId, item.Id);
                }
                catch
                {
                    // Skip items that fail to load
                }
            }
        }

        // Restore equipment
        if (accountData.Equipment is not null)
        {
            foreach (var kvp in accountData.Equipment)
            {
                if (Enum.TryParse<EquipmentSlot>(kvp.Key, out var slot))
                {
                    try
                    {
                        // Handle both old instance IDs and new blueprint IDs
                        var blueprintId = kvp.Value.Contains('#') ? kvp.Value.Split('#')[0] : kvp.Value;
                        var item = await _state.Objects.CloneAsync<IEquippable>(blueprintId, _state);
                        _state.Containers.Add(_playerId, item.Id);
                        _state.Equipment.Equip(_playerId, slot, item.Id);
                    }
                    catch
                    {
                        // Skip equipment that fails to load
                    }
                }
            }
        }

        // Call login hook
        player.OnLogin(ctx);

        // Display any login messages
        DisplayMessages();

        // Show initial room
        await TryExecuteRegisteredCommandAsync("look", Array.Empty<string>());
    }

    private async Task LogoutPlayerAsync()
    {
        if (_playerId is null || _session.PlayerName is null) return;

        var player = _state.Objects!.Get<IPlayer>(_playerId);
        if (player is not null)
        {
            var ctx = CreateContextFor(_playerId);
            player.OnLogout(ctx);
            DisplayMessages();
        }

        try
        {
            // Load existing account data
            var accountData = await _accounts.LoadPlayerDataAsync(_session.PlayerName);
            if (accountData is null) return;

            // Update location
            accountData.Location = GetPlayerLocation();

            // Update state from IStateStore
            var stateStore = _state.Objects.GetStateStore(_playerId);
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
            foreach (var itemId in _state.Containers.GetContents(_playerId))
            {
                // Extract blueprint ID from instance ID (e.g., "Items/sword.cs#000001" -> "Items/sword.cs")
                var blueprintId = itemId.Contains('#') ? itemId.Split('#')[0] : itemId;
                accountData.Inventory.Add(blueprintId);
            }

            // Update equipment (save blueprint IDs, not instance IDs)
            accountData.Equipment.Clear();
            var equipped = _state.Equipment.GetAllEquipped(_playerId);
            foreach (var (slot, itemId) in equipped)
            {
                // Extract blueprint ID from instance ID
                var blueprintId = itemId.Contains('#') ? itemId.Split('#')[0] : itemId;
                accountData.Equipment[slot.ToString()] = blueprintId;
            }

            // Save to file
            await _accounts.SavePlayerDataAsync(_session.PlayerName, accountData);
            Console.WriteLine("Player data saved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving player data: {ex.Message}");
        }
    }

    private string? GetPlayerLocation()
    {
        return _playerId is not null ? _state.Containers.GetContainer(_playerId) : null;
    }

    private MudContext CreateContextFor(string objectId)
    {
        return _state.CreateContext(objectId);
    }

    private async Task<bool> TryExecuteRegisteredCommandAsync(string commandName, string[] args)
    {
        var command = _commandRegistry.GetCommand(commandName);
        if (command is null) return false;

        // Check wizard permission
        if (command.IsWizardOnly && !_session.IsWizard)
        {
            Console.WriteLine("That command requires wizard privileges.");
            return true;  // Command exists but not allowed
        }

        // Create command context
        var context = new Commands.CommandContext
        {
            State = _state,
            PlayerId = _playerId!,
            Session = _session,
            Output = Console.WriteLine,
            CreateContext = CreateContextFor,
            RawInput = string.Join(" ", new[] { commandName }.Concat(args))
        };

        await command.ExecuteAsync(context, args);

        // Display any messages generated by the command
        DisplayMessages();

        return true;
    }

    private void DisplayMessages()
    {
        var messages = _state.Messages.Drain();
        var playerRoomId = GetPlayerLocation();

        foreach (var msg in messages)
        {
            // Filter messages by relevance to player
            var shouldDisplay = msg.Type switch
            {
                MessageType.Tell => msg.ToId == _playerId,
                MessageType.Say => msg.RoomId == playerRoomId,
                MessageType.Emote => msg.RoomId == playerRoomId,
                _ => false
            };

            if (!shouldDisplay) continue;

            // Format message based on type
            var formatted = msg.Type switch
            {
                MessageType.Tell => $"{GetObjectName(msg.FromId)} tells you: {msg.Content}",
                MessageType.Say => $"{GetObjectName(msg.FromId)} says: {msg.Content}",
                MessageType.Emote => $"{GetObjectName(msg.FromId)} {msg.Content}",
                _ => msg.Content
            };

            Console.WriteLine(formatted);
        }
    }

    private string GetObjectName(string objectId)
    {
        // Special case for the current player - use session's PlayerName
        if (objectId == _playerId && _session.PlayerName is not null)
        {
            return _session.PlayerName;
        }

        var obj = _state.Objects!.Get<IMudObject>(objectId);
        return obj?.Name ?? objectId;
    }

    private void ProcessHeartbeats()
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

        // Display any messages generated by heartbeats
        if (dueObjects.Count > 0)
        {
            DisplayMessages();
        }
    }

    private void ProcessCallOuts()
    {
        var dueCallouts = _state.CallOuts.GetDueCallouts();

        foreach (var callout in dueCallouts)
        {
            var obj = _state.Objects!.Get<IMudObject>(callout.TargetId);
            if (obj is null)
            {
                // Object was destructed, skip
                continue;
            }

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

        // Display any messages generated by callouts
        if (dueCallouts.Count > 0)
        {
            DisplayMessages();
        }
    }

    private void ProcessCombat()
    {
        void SendMessage(string targetId, string message)
        {
            if (targetId == _playerId)
            {
                Console.WriteLine(message);
            }
        }

        var deaths = _state.Combat.ProcessCombatRounds(_state, _clock, SendMessage);

        // Handle deaths - award experience, trigger hooks
        foreach (var (killerId, victimId) in deaths)
        {
            // Get victim for experience value (if they're a special type with XP)
            var victim = _state.Objects!.Get<ILiving>(victimId);

            // Award experience if killer is a player
            if (killerId == _playerId)
            {
                var player = _state.Objects.Get<IPlayer>(killerId);
                if (player is not null)
                {
                    // Base XP is victim's MaxHP
                    int xpValue = victim?.MaxHP ?? 10;
                    var ctx = CreateContextFor(killerId);
                    player.AwardExperience(xpValue, ctx);
                    Console.WriteLine($"You gain {xpValue} experience points!");
                }

                // Call OnKill hook
                var killerObj = _state.Objects.Get<IMudObject>(killerId);
                if (killerObj is IOnKill onKill)
                {
                    var ctx = CreateContextFor(killerId);
                    SafeInvoker.TryInvokeHook(() => onKill.OnKill(victimId, ctx), $"OnKill in {killerId}");
                }
            }

            Console.WriteLine($"{victim?.Name ?? victimId} has been slain!");
        }
    }
}
