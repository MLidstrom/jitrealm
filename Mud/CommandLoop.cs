using JitRealm.Mud.Commands;
using JitRealm.Mud.Configuration;
using JitRealm.Mud.Network;
using JitRealm.Mud.Persistence;
using JitRealm.Mud.Security;

namespace JitRealm.Mud;

public sealed class CommandLoop
{
    private readonly WorldState _state;
    private readonly WorldStatePersistence _persistence;
    private readonly DriverSettings _settings;
    private readonly IClock _clock;
    private string? _playerId;
    private readonly ConsoleSession _session;
    private readonly CommandRegistry _commandRegistry;

    public CommandLoop(WorldState state, WorldStatePersistence persistence, DriverSettings settings)
    {
        _state = state;
        _persistence = persistence;
        _settings = settings;
        _clock = state.Clock;
        _session = new ConsoleSession();
        _commandRegistry = CommandFactory.CreateRegistry();
    }

    public async Task RunAsync()
    {
        Console.WriteLine($"{_settings.Server.MudName} v{_settings.Server.Version}");
        Console.WriteLine("Commands: look, go <exit>, get <item>, drop <item>, inventory, examine <item>,");
        Console.WriteLine("          equip <item>, unequip <slot>, equipment, score,");
        Console.WriteLine("          kill <target>, flee, consider <target>,");
        Console.WriteLine("          shout <msg>, whisper <player> <msg>, who, help [cmd],");
        Console.WriteLine("          objects, blueprints, clone <bp>, destruct <id>, stat <id>,");
        Console.WriteLine("          reload <bp>, unload <bp>, reset <id>, save, load, quit");

        // Create player world object
        await CreatePlayerAsync();

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
                switch (cmd)
                {
                    case "look":
                    case "l":
                        if (parts.Length == 1)
                        {
                        await LookAsync();
                        }
                        else
                        {
                            // "look at X" or "look X" or "l X"
                            var target = parts[1].ToLowerInvariant() == "at" && parts.Length > 2
                                ? string.Join(" ", parts.Skip(2))
                                : string.Join(" ", parts.Skip(1));
                            await LookAtDetailAsync(target);
                        }
                        break;

                    case "go":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: go <exit>");
                            break;
                        }
                        await GoAsync(parts[1]);
                        break;

                    case "get":
                    case "take":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: get <item>");
                            break;
                        }
                        GetItem(string.Join(" ", parts.Skip(1)));
                        break;

                    case "drop":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: drop <item>");
                            break;
                        }
                        DropItem(string.Join(" ", parts.Skip(1)));
                        break;

                    case "inventory":
                    case "inv":
                    case "i":
                        ShowInventory();
                        break;

                    case "examine":
                    case "exam":
                    case "x":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: examine <item>");
                            break;
                        }
                        ExamineItem(string.Join(" ", parts.Skip(1)));
                        break;

                    case "objects":
                        Console.WriteLine("=== Instances ===");
                        foreach (var id in _state.Objects!.ListInstanceIds())
                            Console.WriteLine($"  {id}");
                        break;

                    case "blueprints":
                        Console.WriteLine("=== Blueprints ===");
                        foreach (var id in _state.Objects!.ListBlueprintIds())
                            Console.WriteLine($"  {id}");
                        break;

                    case "clone":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: clone <blueprintId>");
                            break;
                        }
                        await CloneAsync(parts[1]);
                        break;

                    case "destruct":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: destruct <objectId>");
                            break;
                        }
                        await DestructAsync(parts[1]);
                        break;

                    case "stat":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: stat <id>");
                            break;
                        }
                        ShowStats(parts[1]);
                        break;

                    case "reload":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: reload <blueprintId>");
                            break;
                        }
                        await _state.Objects!.ReloadBlueprintAsync(parts[1], _state);
                        Console.WriteLine($"Reloaded blueprint {parts[1]}");
                        break;

                    case "unload":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: unload <blueprintId>");
                            break;
                        }
                        await _state.Objects!.UnloadBlueprintAsync(parts[1], _state);
                        Console.WriteLine($"Unloaded blueprint {parts[1]}");
                        break;

                    case "reset":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: reset <objectId>");
                            break;
                        }
                        await ResetAsync(parts[1]);
                        break;

                    case "save":
                        await SaveAsync();
                        break;

                    case "load":
                        await LoadSaveAsync();
                        break;

                    case "score":
                        ShowScore();
                        break;

                    case "equip":
                    case "wield":
                    case "wear":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: equip <item>");
                            break;
                        }
                        EquipItem(string.Join(" ", parts.Skip(1)));
                        break;

                    case "unequip":
                    case "remove":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: unequip <slot>");
                            break;
                        }
                        UnequipSlot(string.Join(" ", parts.Skip(1)));
                        break;

                    case "equipment":
                    case "eq":
                        ShowEquipment();
                        break;

                    case "kill":
                    case "attack":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: kill <target>");
                            break;
                        }
                        StartCombat(string.Join(" ", parts.Skip(1)));
                        break;

                    case "flee":
                    case "retreat":
                        AttemptFlee();
                        break;

                    case "consider":
                    case "con":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: consider <target>");
                            break;
                        }
                        ConsiderTarget(string.Join(" ", parts.Skip(1)));
                        break;

                    case "quit":
                    case "exit":
                        await LogoutPlayerAsync();
                        return;

                    default:
                        // Try the command registry for social/utility commands
                        if (!await TryExecuteRegisteredCommandAsync(cmd, parts.Skip(1).ToArray()))
                        {
                            Console.WriteLine("Unknown command. Type 'help' for a list of commands.");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private async Task CreatePlayerAsync()
    {
        // Load start room first
        var startRoom = await _state.Objects!.LoadAsync<IRoom>(_settings.Paths.StartRoom, _state);

        // Process spawns for the start room
        await _state.ProcessSpawnsAsync(startRoom.Id, _clock);

        // Clone a player from the player blueprint
        var player = await _state.Objects.CloneAsync<IPlayer>(_settings.Paths.PlayerBlueprint, _state);
        _playerId = player.Id;

        // Set up the session
        _session.PlayerId = _playerId;
        _session.PlayerName = "Player";
        _state.Sessions.Add(_session);

        // Set player name
        var ctx = CreateContextFor(_playerId);
        if (player is PlayerBase playerBase)
        {
            playerBase.SetPlayerName("Player", ctx);
        }

        // Move player to start room
        _state.Containers.Add(_playerId, startRoom.Id);

        // Call login hook
        player.OnLogin(ctx);

        // Display any login messages
        DisplayMessages();

        // Show initial room
        await LookAsync();
    }

    private Task LogoutPlayerAsync()
    {
        if (_playerId is null) return Task.CompletedTask;

        var player = _state.Objects!.Get<IPlayer>(_playerId);
        if (player is not null)
        {
            var ctx = CreateContextFor(_playerId);
            player.OnLogout(ctx);
            DisplayMessages();
        }
        return Task.CompletedTask;
    }

    private async Task CloneAsync(string blueprintId)
    {
        var instance = await _state.Objects!.CloneAsync<IMudObject>(blueprintId, _state);
        Console.WriteLine($"Created clone: {instance.Id}");

        // If cloning into current room, add to room contents
        var playerRoomId = GetPlayerLocation();
        if (playerRoomId is not null)
        {
            _state.Containers.Add(instance.Id, playerRoomId);
            Console.WriteLine($"  (placed in {playerRoomId})");
        }
    }

    private async Task DestructAsync(string objectId)
    {
        // Remove from any container first
        _state.Containers.Remove(objectId);

        await _state.Objects!.DestructAsync(objectId, _state);
        Console.WriteLine($"Destructed: {objectId}");
    }

    private void ShowStats(string id)
    {
        var stats = _state.Objects!.GetStats(id);
        if (stats is null)
        {
            Console.WriteLine($"Object not found: {id}");
            return;
        }

        Console.WriteLine($"=== {stats.Id} ===");
        Console.WriteLine($"  Type: {(stats.IsBlueprint ? "Blueprint" : "Instance")}");
        Console.WriteLine($"  Blueprint: {stats.BlueprintId}");
        Console.WriteLine($"  Class: {stats.TypeName}");

        if (stats.IsBlueprint)
        {
            Console.WriteLine($"  Source mtime: {stats.SourceMtime}");
            Console.WriteLine($"  Instance count: {stats.InstanceCount}");
        }
        else
        {
            Console.WriteLine($"  Created: {stats.CreatedAt}");
            if (stats.StateKeys?.Length > 0)
                Console.WriteLine($"  State keys: {string.Join(", ", stats.StateKeys)}");

            // Show living stats if applicable
            var obj = _state.Objects.Get<IMudObject>(id);
            if (obj is ILiving living)
            {
                Console.WriteLine($"  HP: {living.HP}/{living.MaxHP}");
                Console.WriteLine($"  Alive: {living.IsAlive}");
            }
            if (obj is IPlayer player)
            {
                Console.WriteLine($"  Level: {player.Level}");
                Console.WriteLine($"  Experience: {player.Experience}");
            }
        }
    }

    private void ShowScore()
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

        var player = _state.Objects!.Get<IPlayer>(_playerId);
        if (player is null)
        {
            Console.WriteLine("Player not found.");
            return;
        }

        Console.WriteLine($"=== {player.PlayerName} ===");
        Console.WriteLine($"  HP: {player.HP}/{player.MaxHP}");
        Console.WriteLine($"  Level: {player.Level}");
        Console.WriteLine($"  Experience: {player.Experience}");
        Console.WriteLine($"  Session time: {player.SessionTime:hh\\:mm\\:ss}");
    }

    private async Task LookAsync()
    {
        var room = await GetCurrentRoomAsync();
        Console.WriteLine(room.Name);
        Console.WriteLine(room.Description);

        if (room.Exits.Count > 0)
            Console.WriteLine("Exits: " + string.Join(", ", room.Exits.Keys));

        // Get contents from driver's container registry
        var contents = _state.Containers.GetContents(room.Id);
        if (contents.Count > 0)
        {
            var names = new List<string>();
            foreach (var objId in contents)
            {
                // Don't show the player themselves
                if (objId == _playerId) continue;

                var obj = _state.Objects!.Get<IMudObject>(objId);
                names.Add(obj?.Name ?? objId);
            }
            if (names.Count > 0)
                Console.WriteLine("You see: " + string.Join(", ", names));
        }
    }

    private async Task LookAtDetailAsync(string target)
    {
        var room = await GetCurrentRoomAsync();
        var normalizedTarget = target.ToLowerInvariant();

        // 1. Check room details first
        foreach (var (keyword, description) in room.Details)
        {
            if (keyword.ToLowerInvariant().Contains(normalizedTarget) ||
                normalizedTarget.Contains(keyword.ToLowerInvariant()))
            {
                Console.WriteLine(description);
                return;
            }
        }

        // 2. Check items in inventory
        var ctx = CreateContextFor(_playerId!);
        var itemId = ctx.FindItem(target, _playerId!);
        if (itemId is not null)
        {
            var item = _state.Objects!.Get<IItem>(itemId);
            if (item is not null)
            {
                // Check item details first
                foreach (var (keyword, description) in item.Details)
                {
                    if (keyword.ToLowerInvariant().Contains(normalizedTarget) ||
                        normalizedTarget.Contains(keyword.ToLowerInvariant()))
                    {
                        Console.WriteLine(description);
                        return;
                    }
                }
                // Fall back to item long description
                Console.WriteLine(item.LongDescription);
                return;
            }
        }

        // 3. Check all objects in room by name and aliases
        var contents = _state.Containers.GetContents(room.Id);
        foreach (var objId in contents)
        {
            if (objId == _playerId) continue;

            var obj = _state.Objects!.Get<IMudObject>(objId);
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
                        Console.WriteLine(description);
                        return;
                    }
                }
                // For items, show long description
                if (obj is IItem item)
                {
                    Console.WriteLine(item.LongDescription);
                    return;
                }
                // For livings, show their description and HP
                if (obj is ILiving living)
                {
                    Console.WriteLine(living.Description);
                    Console.WriteLine($"  HP: {living.HP}/{living.MaxHP}");
                    return;
                }
                Console.WriteLine($"You see {obj.Name}.");
                return;
            }
        }

        Console.WriteLine($"You don't see '{target}' here.");
    }

    private async Task GoAsync(string exit)
    {
        var currentRoom = await GetCurrentRoomAsync();
        if (!currentRoom.Exits.TryGetValue(exit, out var destId))
        {
            Console.WriteLine("You can't go that way.");
            return;
        }

        // Call IOnLeave hook on current room (with timeout protection)
        if (currentRoom is IOnLeave onLeave)
        {
            var ctx = CreateContextFor(currentRoom.Id);
            SafeInvoker.TryInvokeHook(() => onLeave.OnLeave(ctx, _playerId!), $"OnLeave in {currentRoom.Id}");
        }

        var dest = await _state.Objects!.LoadAsync<IRoom>(destId, _state);

        // Process spawns for the destination room
        await _state.ProcessSpawnsAsync(dest.Id, _clock);

        // Move player to new room via ContainerRegistry
        _state.Containers.Move(_playerId!, dest.Id);

        // Call IOnEnter hook on destination room (with timeout protection)
        if (dest is IOnEnter onEnter)
        {
            var ctx = CreateContextFor(dest.Id);
            SafeInvoker.TryInvokeHook(() => onEnter.OnEnter(ctx, _playerId!), $"OnEnter in {dest.Id}");
        }

        // Display any messages generated by the hooks
        DisplayMessages();

        await LookAsync();
    }

    private string? GetPlayerLocation()
    {
        return _playerId is not null ? _state.Containers.GetContainer(_playerId) : null;
    }

    private async Task<IRoom> GetCurrentRoomAsync()
    {
        var roomId = GetPlayerLocation() ?? throw new InvalidOperationException("Player has no location.");
        return _state.Objects!.Get<IRoom>(roomId) ?? await _state.Objects.LoadAsync<IRoom>(roomId, _state);
    }

    private async Task ResetAsync(string objectId)
    {
        var obj = _state.Objects!.Get<IMudObject>(objectId);
        if (obj is null)
        {
            Console.WriteLine($"Object not found: {objectId}");
            return;
        }

        if (obj is not IResettable resettable)
        {
            Console.WriteLine($"Object {objectId} does not implement IResettable");
            return;
        }

        var ctx = CreateContextFor(objectId);
        if (SafeInvoker.TryInvokeHook(() => resettable.Reset(ctx), $"Reset in {objectId}"))
        {
            Console.WriteLine($"Reset: {objectId}");
        }

        // Process spawns if this is a room with ISpawner
        if (obj is ISpawner)
        {
            var spawned = await _state.ProcessSpawnsAsync(objectId, _clock);
            if (spawned > 0)
            {
                Console.WriteLine($"Spawned {spawned} creature(s).");
            }
        }

        // Display any messages generated by the reset
        DisplayMessages();
    }

    private async Task SaveAsync()
    {
        await _persistence.SaveAsync(_state, _session);
        Console.WriteLine("World state saved.");
    }

    private async Task LoadSaveAsync()
    {
        var loaded = await _persistence.LoadAsync(_state, _session);
        if (loaded)
        {
            // Restore player ID from session
            _playerId = _session.PlayerId;

            Console.WriteLine("World state loaded.");

            // Re-look at current location if player has one
            if (GetPlayerLocation() is not null)
            {
                await LookAsync();
            }
        }
        else
        {
            Console.WriteLine("No saved state found.");
        }
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

    private void GetItem(string itemName)
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

        var roomId = GetPlayerLocation();
        if (roomId is null)
        {
            Console.WriteLine("You're not in a room.");
            return;
        }

        // Find item in room
        var ctx = CreateContextFor(_playerId);
        var itemId = ctx.FindItem(itemName, roomId);
        if (itemId is null)
        {
            Console.WriteLine($"You don't see '{itemName}' here.");
            return;
        }

        // Check if it's a carryable item
        var item = _state.Objects!.Get<IItem>(itemId);
        if (item is null)
        {
            Console.WriteLine("That's not an item.");
            return;
        }

        // Check weight limit
        var player = _state.Objects.Get<IPlayer>(_playerId);
        if (player is not null && !player.CanCarry(item.Weight))
        {
            Console.WriteLine($"You can't carry that much weight. (Carrying {player.CarriedWeight}/{player.CarryCapacity})");
            return;
        }

        // Move item to player inventory
        ctx.Move(itemId, _playerId);
        Console.WriteLine($"You pick up {item.ShortDescription}.");
        DisplayMessages();
    }

    private void DropItem(string itemName)
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

        var roomId = GetPlayerLocation();
        if (roomId is null)
        {
            Console.WriteLine("You're not in a room.");
            return;
        }

        // Find item in inventory
        var ctx = CreateContextFor(_playerId);
        var itemId = ctx.FindItem(itemName, _playerId);
        if (itemId is null)
        {
            Console.WriteLine($"You're not carrying '{itemName}'.");
            return;
        }

        var item = _state.Objects!.Get<IItem>(itemId);
        if (item is null)
        {
            Console.WriteLine("That's not an item.");
            return;
        }

        // Move item to room
        ctx.Move(itemId, roomId);
        Console.WriteLine($"You drop {item.ShortDescription}.");
        DisplayMessages();
    }

    private void ShowInventory()
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

        var contents = _state.Containers.GetContents(_playerId);
        if (contents.Count == 0)
        {
            Console.WriteLine("You are not carrying anything.");
            return;
        }

        Console.WriteLine("You are carrying:");
        int totalWeight = 0;
        foreach (var itemId in contents)
        {
            var item = _state.Objects!.Get<IItem>(itemId);
            if (item is not null)
            {
                Console.WriteLine($"  {item.ShortDescription} ({item.Weight} lbs)");
                totalWeight += item.Weight;
            }
            else
            {
                var obj = _state.Objects.Get<IMudObject>(itemId);
                Console.WriteLine($"  {obj?.Name ?? itemId}");
            }
        }

        var player = _state.Objects!.Get<IPlayer>(_playerId);
        if (player is not null)
        {
            Console.WriteLine($"Total weight: {totalWeight}/{player.CarryCapacity} lbs");
        }
    }

    private void ExamineItem(string itemName)
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

        var roomId = GetPlayerLocation();
        if (roomId is null)
        {
            Console.WriteLine("You're not in a room.");
            return;
        }

        // Find item in room or inventory
        var ctx = CreateContextFor(_playerId);
        var itemId = ctx.FindItem(itemName, _playerId);  // Check inventory first
        if (itemId is null)
        {
            itemId = ctx.FindItem(itemName, roomId);  // Then check room
        }

        if (itemId is null)
        {
            Console.WriteLine($"You don't see '{itemName}' here.");
            return;
        }

        var item = _state.Objects!.Get<IItem>(itemId);
        if (item is not null)
        {
            Console.WriteLine(item.LongDescription);
            Console.WriteLine($"  Weight: {item.Weight} lbs");
            Console.WriteLine($"  Value: {item.Value} coins");
        }
        else
        {
            var obj = _state.Objects.Get<IMudObject>(itemId);
            if (obj is not null)
            {
                Console.WriteLine($"{obj.Name}");
            }
            else
            {
                Console.WriteLine("You examine it closely but see nothing special.");
            }
        }
    }

    private void EquipItem(string itemName)
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

        // Find item in inventory
        var ctx = CreateContextFor(_playerId);
        var itemId = ctx.FindItem(itemName, _playerId);
        if (itemId is null)
        {
            Console.WriteLine($"You're not carrying '{itemName}'.");
            return;
        }

        var item = _state.Objects!.Get<IEquippable>(itemId);
        if (item is null)
        {
            Console.WriteLine("That can't be equipped.");
            return;
        }

        // Check if something is already equipped in that slot
        var existingItemId = _state.Equipment.GetEquipped(_playerId, item.Slot);
        if (existingItemId is not null)
        {
            var existingItem = _state.Objects.Get<IEquippable>(existingItemId);
            if (existingItem is not null)
            {
                // Unequip existing item first
                var existingItemCtx = CreateContextFor(existingItemId);
                existingItem.OnUnequip(_playerId, existingItemCtx);
                Console.WriteLine($"You remove {existingItem.ShortDescription}.");
            }
        }

        // Equip the new item
        _state.Equipment.Equip(_playerId, item.Slot, itemId);

        // Call OnEquip hook
        var itemCtx = CreateContextFor(itemId);
        item.OnEquip(_playerId, itemCtx);

        Console.WriteLine($"You equip {item.ShortDescription} ({item.Slot}).");
        DisplayMessages();
    }

    private void UnequipSlot(string slotName)
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

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
                Console.WriteLine($"Ambiguous slot '{slotName}'. Did you mean: {string.Join(", ", matchingSlots)}?");
                return;
            }
            else
            {
                Console.WriteLine($"Unknown slot '{slotName}'. Valid slots: {string.Join(", ", Enum.GetNames<EquipmentSlot>())}");
                return;
            }
        }

        // Check if something is equipped in that slot
        var itemId = _state.Equipment.GetEquipped(_playerId, slot);
        if (itemId is null)
        {
            Console.WriteLine($"Nothing is equipped in {slot}.");
            return;
        }

        var item = _state.Objects!.Get<IEquippable>(itemId);
        if (item is not null)
        {
            // Call OnUnequip hook
            var itemCtx = CreateContextFor(itemId);
            item.OnUnequip(_playerId, itemCtx);
        }

        // Unequip the item
        _state.Equipment.Unequip(_playerId, slot);

        Console.WriteLine($"You unequip {item?.ShortDescription ?? itemId}.");
        DisplayMessages();
    }

    private void ShowEquipment()
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

        var equipped = _state.Equipment.GetAllEquipped(_playerId);
        if (equipped.Count == 0)
        {
            Console.WriteLine("You have nothing equipped.");
            return;
        }

        Console.WriteLine("You have equipped:");
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

                Console.WriteLine($"  {slot,-12}: {desc}");
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
            Console.WriteLine();
            if (totalAC > 0) Console.WriteLine($"Total Armor Class: {totalAC}");
            if (maxDmg > 0) Console.WriteLine($"Weapon Damage: {minDmg}-{maxDmg}");
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

    private void StartCombat(string targetName)
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

        // Check if already in combat
        if (_state.Combat.IsInCombat(_playerId))
        {
            var currentTarget = _state.Combat.GetCombatTarget(_playerId);
            var currentTargetObj = _state.Objects!.Get<IMudObject>(currentTarget!);
            Console.WriteLine($"You are already fighting {currentTargetObj?.Name ?? currentTarget}!");
            return;
        }

        var roomId = GetPlayerLocation();
        if (roomId is null)
        {
            Console.WriteLine("You're not in a room.");
            return;
        }

        // Find target in room
        var targetId = FindTargetInRoom(targetName, roomId);
        if (targetId is null)
        {
            Console.WriteLine($"You don't see '{targetName}' here.");
            return;
        }

        // Can't attack yourself
        if (targetId == _playerId)
        {
            Console.WriteLine("You can't attack yourself!");
            return;
        }

        // Check if target is a living thing
        var target = _state.Objects!.Get<ILiving>(targetId);
        if (target is null)
        {
            Console.WriteLine("You can't attack that.");
            return;
        }

        if (!target.IsAlive)
        {
            Console.WriteLine($"{target.Name} is already dead.");
            return;
        }

        // Start combat
        _state.Combat.StartCombat(_playerId, targetId, _clock.Now);

        Console.WriteLine($"You attack {target.Name}!");

        // If target is not already in combat, they fight back
        if (!_state.Combat.IsInCombat(targetId))
        {
            _state.Combat.StartCombat(targetId, _playerId, _clock.Now);
        }
    }

    private void AttemptFlee()
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

        if (!_state.Combat.IsInCombat(_playerId))
        {
            Console.WriteLine("You're not in combat.");
            return;
        }

        var exitDir = _state.Combat.AttemptFlee(_playerId, _state, _clock);

        if (exitDir is null)
        {
            Console.WriteLine("You fail to escape!");
            return;
        }

        Console.WriteLine($"You flee to the {exitDir}!");

        // Actually move the player
        Task.Run(async () => await GoAsync(exitDir)).Wait();
    }

    private void ConsiderTarget(string targetName)
    {
        if (_playerId is null)
        {
            Console.WriteLine("No player.");
            return;
        }

        var roomId = GetPlayerLocation();
        if (roomId is null)
        {
            Console.WriteLine("You're not in a room.");
            return;
        }

        // Find target in room
        var targetId = FindTargetInRoom(targetName, roomId);
        if (targetId is null)
        {
            Console.WriteLine($"You don't see '{targetName}' here.");
            return;
        }

        var target = _state.Objects!.Get<ILiving>(targetId);
        if (target is null)
        {
            Console.WriteLine("You can't fight that.");
            return;
        }

        var player = _state.Objects.Get<ILiving>(_playerId);
        if (player is null)
        {
            Console.WriteLine("Error getting player stats.");
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

        Console.WriteLine($"{target.Name} looks like {difficulty}.");
        Console.WriteLine($"  HP: {target.HP}/{target.MaxHP}");

        if (target is IHasEquipment equipped)
        {
            Console.WriteLine($"  Armor Class: {equipped.TotalArmorClass}");
            var (min, max) = equipped.WeaponDamage;
            if (max > 0)
            {
                Console.WriteLine($"  Weapon Damage: {min}-{max}");
            }
        }
    }

    private string? FindTargetInRoom(string name, string roomId)
    {
        if (_state.Objects is null)
            return null;

        var normalizedName = name.ToLowerInvariant();
        var contents = _state.Containers.GetContents(roomId);

        foreach (var objId in contents)
        {
            if (objId == _playerId)
                continue;  // Skip self

            var obj = _state.Objects.Get<IMudObject>(objId);
            if (obj is null)
                continue;

            // Check if object name contains the search term (case-insensitive)
            if (obj.Name.ToLowerInvariant().Contains(normalizedName))
                return objId;
        }

        return null;
    }
}
