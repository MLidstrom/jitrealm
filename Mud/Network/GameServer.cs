using JitRealm.Mud.Configuration;
using JitRealm.Mud.Persistence;
using JitRealm.Mud.Security;

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
    private bool _running;

    public GameServer(WorldState state, WorldStatePersistence persistence, DriverSettings settings)
    {
        _state = state;
        _persistence = persistence;
        _settings = settings;
        _telnet = new TelnetServer(settings.Server.Port);

        _telnet.OnClientConnected += OnClientConnected;
    }

    public int Port => _telnet.Port;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _telnet.Start();
        _running = true;

        Console.WriteLine($"{_settings.Server.MudName} v{_settings.Server.Version} - Multi-user server");
        Console.WriteLine($"Listening on port {_telnet.Port}...");
        Console.WriteLine("Press Ctrl+C to stop.");

        try
        {
            while (_running && !cancellationToken.IsCancellationRequested)
            {
                // Accept any pending connections
                await _telnet.AcceptPendingConnectionsAsync();

                // Process heartbeats (world-wide)
                ProcessHeartbeats();

                // Process callouts (world-wide)
                ProcessCallOuts();

                // Process combat rounds (world-wide)
                ProcessCombat();

                // Process input from all sessions
                await ProcessAllSessionsAsync();

                // Deliver messages to sessions
                DeliverMessages();

                // Prune disconnected sessions
                _state.Sessions.PruneDisconnected();

                // Small delay to prevent busy-loop
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

        // Clone a player world object for this session
        var playerName = $"Player{_state.Sessions.Count + 1}";

        try
        {
            // Load start room
            var startRoom = await _state.Objects!.LoadAsync<IRoom>(_settings.Paths.StartRoom, _state);

            // Process spawns for the start room
            await _state.ProcessSpawnsAsync(startRoom.Id, new SystemClock());

            // Clone player from blueprint
            var player = await _state.Objects.CloneAsync<IPlayer>(_settings.Paths.PlayerBlueprint, _state);

            // Set up the session
            session.PlayerId = player.Id;
            session.PlayerName = playerName;

            // Set player name via context
            var ctx = CreateContextFor(player.Id);
            if (player is PlayerBase playerBase)
            {
                playerBase.SetPlayerName(playerName, ctx);
            }

            // Move player to start room
            _state.Containers.Add(startRoom.Id, player.Id);

            // Register session
            _state.Sessions.Add(session);

            // Call login hook
            player.OnLogin(ctx);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{session.SessionId}] Failed to create player: {ex.Message}");
            await session.CloseAsync();
            return;
        }

        // Welcome message
        var welcomeMsg = _settings.Server.WelcomeMessage.Replace("{PlayerName}", playerName);
        await session.WriteLineAsync(welcomeMsg);
        await session.WriteLineAsync("Type 'help' for commands, 'quit' to disconnect.");
        await session.WriteLineAsync("");

        // Show initial room
        await ShowRoomAsync(session);
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

                if (string.IsNullOrWhiteSpace(input)) continue;

                await ProcessCommandAsync(session, input.Trim());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{session.SessionId}] Error: {ex.Message}");
            }
        }
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
                await ShowRoomAsync(session);
                break;

            case "go":
                if (parts.Length < 2)
                {
                    await session.WriteLineAsync("Usage: go <exit>");
                    break;
                }
                await GoAsync(session, parts[1]);
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
                break;

            case "who":
                await ShowWhoAsync(session);
                break;

            case "score":
                await ShowScoreAsync(session);
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

            case "help":
                await session.WriteLineAsync("Commands: look, go <exit>, get <item>, drop <item>, inventory,");
                await session.WriteLineAsync("          examine <item>, equip <item>, unequip <slot>, equipment,");
                await session.WriteLineAsync("          kill <target>, flee, consider <target>,");
                await session.WriteLineAsync("          say <msg>, who, score, quit");
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

            case "quit":
            case "exit":
                Console.WriteLine($"[{session.SessionId}] {playerName} disconnected");

                // Call logout hook
                var player = _state.Objects!.Get<IPlayer>(playerId);
                if (player is not null)
                {
                    var ctx = CreateContextFor(playerId);
                    player.OnLogout(ctx);
                }

                BroadcastToRoom(playerLocation!, $"{playerName} has left the realm.", session);

                // Remove player from room
                _state.Containers.Remove(playerId);

                await session.CloseAsync();
                _state.Sessions.Remove(session.SessionId);
                break;

            default:
                await session.WriteLineAsync("Unknown command. Type 'help' for a list of commands.");
                break;
        }
    }

    private async Task ShowRoomAsync(ISession session)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("You are nowhere.");
            return;
        }

        var roomId = _state.Containers.GetContainer(playerId);
        if (roomId is null)
        {
            await session.WriteLineAsync("You are nowhere.");
            return;
        }

        var room = _state.Objects!.Get<IRoom>(roomId);
        if (room is null)
        {
            await session.WriteLineAsync("You are nowhere.");
            return;
        }

        await session.WriteLineAsync(room.Name);
        await session.WriteLineAsync(room.Description);

        if (room.Exits.Count > 0)
            await session.WriteLineAsync("Exits: " + string.Join(", ", room.Exits.Keys));

        // Show other players and objects in room
        var contents = _state.Containers.GetContents(roomId);
        var players = new List<string>();
        var objects = new List<string>();

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
            else if (obj is not null)
            {
                objects.Add(obj.Name);
            }
        }

        if (players.Count > 0)
            await session.WriteLineAsync("Players here: " + string.Join(", ", players));

        if (objects.Count > 0)
            await session.WriteLineAsync("You see: " + string.Join(", ", objects));
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

        // Move to destination
        var destRoom = await _state.Objects.LoadAsync<IRoom>(destId, _state);

        // Process spawns for the destination room
        await _state.ProcessSpawnsAsync(destRoom.Id, new SystemClock());

        _state.Containers.Move(playerId, destRoom.Id);

        // Notify others in new room
        BroadcastToRoom(destRoom.Id, $"{playerName} has arrived.", session);

        // Show new room
        await ShowRoomAsync(session);
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
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var player = _state.Objects!.Get<IPlayer>(playerId);
        if (player is null)
        {
            await session.WriteLineAsync("Player not found.");
            return;
        }

        await session.WriteLineAsync($"=== {player.PlayerName} ===");
        await session.WriteLineAsync($"  HP: {player.HP}/{player.MaxHP}");
        await session.WriteLineAsync($"  Level: {player.Level}");
        await session.WriteLineAsync($"  Experience: {player.Experience}");
        await session.WriteLineAsync($"  Session time: {player.SessionTime:hh\\:mm\\:ss}");
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
    }

    private void ProcessCallOuts()
    {
        var dueCallouts = _state.CallOuts.GetDueCallouts();

        foreach (var callout in dueCallouts)
        {
            var obj = _state.Objects!.Get<IMudObject>(callout.TargetId);
            if (obj is null) continue;

            var method = obj.GetType().GetMethod(callout.MethodName);
            if (method is null)
            {
                Console.WriteLine($"[CallOut error in {callout.TargetId}]: Method '{callout.MethodName}' not found");
                continue;
            }

            var ctx = CreateContextFor(callout.TargetId);
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            if (parameters.Length > 0 && parameters[0].ParameterType == typeof(IMudContext))
            {
                args[0] = ctx;
                if (callout.Args is not null)
                {
                    for (int i = 1; i < parameters.Length && i - 1 < callout.Args.Length; i++)
                        args[i] = callout.Args[i - 1];
                }
            }
            else if (callout.Args is not null)
            {
                for (int i = 0; i < parameters.Length && i < callout.Args.Length; i++)
                    args[i] = callout.Args[i];
            }

            SafeInvoker.TryInvokeCallout(
                () => method.Invoke(obj, args),
                $"CallOut {callout.MethodName} in {callout.TargetId}");
        }
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
        var stateStore = _state.Objects!.GetStateStore(objectId) ?? new DictionaryStateStore();
        var clock = new SystemClock();
        return new MudContext(_state, clock)
        {
            State = stateStore,
            CurrentObjectId = objectId,
            RoomId = _state.Containers.GetContainer(objectId) ?? objectId
        };
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
    }

    private async Task ShowInventoryAsync(ISession session)
    {
        var playerId = session.PlayerId;
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var contents = _state.Containers.GetContents(playerId);
        if (contents.Count == 0)
        {
            await session.WriteLineAsync("You are not carrying anything.");
            return;
        }

        await session.WriteLineAsync("You are carrying:");
        int totalWeight = 0;
        foreach (var itemId in contents)
        {
            var item = _state.Objects!.Get<IItem>(itemId);
            if (item is not null)
            {
                await session.WriteLineAsync($"  {item.ShortDescription} ({item.Weight} lbs)");
                totalWeight += item.Weight;
            }
            else
            {
                var obj = _state.Objects.Get<IMudObject>(itemId);
                await session.WriteLineAsync($"  {obj?.Name ?? itemId}");
            }
        }

        var player = _state.Objects!.Get<IPlayer>(playerId);
        if (player is not null)
        {
            await session.WriteLineAsync($"Total weight: {totalWeight}/{player.CarryCapacity} lbs");
        }
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
        if (playerId is null)
        {
            await session.WriteLineAsync("No player.");
            return;
        }

        var equipped = _state.Equipment.GetAllEquipped(playerId);
        if (equipped.Count == 0)
        {
            await session.WriteLineAsync("You have nothing equipped.");
            return;
        }

        await session.WriteLineAsync("You have equipped:");
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            if (equipped.TryGetValue(slot, out var itemId))
            {
                var item = _state.Objects!.Get<IItem>(itemId);
                var desc = item?.ShortDescription ?? itemId;

                // Add extra info for weapons/armor
                if (item is IWeapon weapon)
                {
                    desc += $" ({weapon.MinDamage}-{weapon.MaxDamage} dmg)";
                }
                else if (item is IArmor armor)
                {
                    desc += $" ({armor.ArmorClass} AC)";
                }

                await session.WriteLineAsync($"  {slot,-12}: {desc}");
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
            await session.WriteLineAsync("");
            if (totalAC > 0) await session.WriteLineAsync($"Total Armor Class: {totalAC}");
            if (maxDmg > 0) await session.WriteLineAsync($"Weapon Damage: {minDmg}-{maxDmg}");
        }
    }

    private void ProcessCombat()
    {
        var clock = new SystemClock();

        void SendMessage(string targetId, string message)
        {
            var targetSession = _state.Sessions.GetByPlayerId(targetId);
            if (targetSession is not null)
            {
                _ = targetSession.WriteLineAsync(message);
            }
        }

        var deaths = _state.Combat.ProcessCombatRounds(_state, clock, SendMessage);

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
        var clock = new SystemClock();
        _state.Combat.StartCombat(playerId, targetId, clock.Now);

        await session.WriteLineAsync($"You attack {target.Name}!");
        BroadcastToRoom(roomId, $"{session.PlayerName} attacks {target.Name}!", session);

        // If target is not already in combat, they fight back
        if (!_state.Combat.IsInCombat(targetId))
        {
            _state.Combat.StartCombat(targetId, playerId, clock.Now);
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

        var clock = new SystemClock();
        var exitDir = _state.Combat.AttemptFlee(playerId, _state, clock);

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
}
