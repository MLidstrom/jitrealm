using JitRealm.Mud.Network;
using JitRealm.Mud.Persistence;
using JitRealm.Mud.Security;

namespace JitRealm.Mud;

public sealed class CommandLoop
{
    private readonly WorldState _state;
    private readonly WorldStatePersistence _persistence;
    private readonly string _startRoomId;
    private string? _playerId;
    private readonly ConsoleSession _session;

    public CommandLoop(WorldState state, WorldStatePersistence persistence, string startRoomId = "Rooms/start")
    {
        _state = state;
        _persistence = persistence;
        _startRoomId = startRoomId;
        _session = new ConsoleSession();
    }

    public async Task RunAsync()
    {
        Console.WriteLine("JitRealm v0.11");
        Console.WriteLine("Commands: look, go <exit>, get <item>, drop <item>, inventory, examine <item>,");
        Console.WriteLine("          equip <item>, unequip <slot>, equipment, score,");
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
                        await LookAsync();
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

                    case "quit":
                    case "exit":
                        await LogoutPlayerAsync();
                        return;

                    default:
                        Console.WriteLine("Unknown command.");
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
        var startRoom = await _state.Objects!.LoadAsync<IRoom>(_startRoomId, _state);

        // Clone a player from the player blueprint
        var player = await _state.Objects.CloneAsync<IPlayer>("std/player", _state);
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

    private Task ResetAsync(string objectId)
    {
        var obj = _state.Objects!.Get<IMudObject>(objectId);
        if (obj is null)
        {
            Console.WriteLine($"Object not found: {objectId}");
            return Task.CompletedTask;
        }

        if (obj is not IResettable resettable)
        {
            Console.WriteLine($"Object {objectId} does not implement IResettable");
            return Task.CompletedTask;
        }

        var ctx = CreateContextFor(objectId);
        if (SafeInvoker.TryInvokeHook(() => resettable.Reset(ctx), $"Reset in {objectId}"))
        {
            Console.WriteLine($"Reset: {objectId}");
        }

        // Display any messages generated by the reset
        DisplayMessages();
        return Task.CompletedTask;
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
        // Get the actual state store from the instance
        var stateStore = _state.Objects!.GetStateStore(objectId) ?? new DictionaryStateStore();

        var clock = new SystemClock();
        return new MudContext(_state, clock)
        {
            State = stateStore,
            CurrentObjectId = objectId,
            RoomId = _state.Containers.GetContainer(objectId) ?? objectId
        };
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

            // Find and invoke the method via reflection
            var method = obj.GetType().GetMethod(callout.MethodName);
            if (method is null)
            {
                Console.WriteLine($"[CallOut error in {callout.TargetId}]: Method '{callout.MethodName}' not found");
                continue;
            }

            var ctx = CreateContextFor(callout.TargetId);

            // Build argument list - first param is always IMudContext
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            if (parameters.Length > 0 && parameters[0].ParameterType == typeof(IMudContext))
            {
                args[0] = ctx;

                // Fill remaining args from callout.Args
                if (callout.Args is not null)
                {
                    for (int i = 1; i < parameters.Length && i - 1 < callout.Args.Length; i++)
                    {
                        args[i] = callout.Args[i - 1];
                    }
                }
            }
            else if (callout.Args is not null)
            {
                // No context parameter, just pass args directly
                for (int i = 0; i < parameters.Length && i < callout.Args.Length; i++)
                {
                    args[i] = callout.Args[i];
                }
            }

            SafeInvoker.TryInvokeCallout(
                () => method.Invoke(obj, args),
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
}
