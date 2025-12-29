using JitRealm.Mud.Persistence;

namespace JitRealm.Mud;

public sealed class CommandLoop
{
    private readonly WorldState _state;
    private readonly WorldStatePersistence _persistence;

    public CommandLoop(WorldState state, WorldStatePersistence persistence)
    {
        _state = state;
        _persistence = persistence;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("JitRealm v0.5");
        Console.WriteLine("Commands: look, go <exit>, objects, blueprints, clone <bp>, destruct <id>, stat <id>,");
        Console.WriteLine("          reload <bp>, unload <bp>, reset <id>, save, load, quit");

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

                    case "quit":
                    case "exit":
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

    private async Task CloneAsync(string blueprintId)
    {
        var instance = await _state.Objects!.CloneAsync<IMudObject>(blueprintId, _state);
        Console.WriteLine($"Created clone: {instance.Id}");
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
        }
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
                var obj = _state.Objects!.Get<IMudObject>(objId);
                names.Add(obj?.Name ?? objId);
            }
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

        // Call IOnLeave hook on current room
        if (currentRoom is IOnLeave onLeave)
        {
            var ctx = CreateContextFor(currentRoom.Id);
            onLeave.OnLeave(ctx, "player");
        }

        var dest = await _state.Objects!.LoadAsync<IRoom>(destId, _state);
        _state.Player!.LocationId = dest.Id;

        // Call IOnEnter hook on destination room
        if (dest is IOnEnter onEnter)
        {
            var ctx = CreateContextFor(dest.Id);
            onEnter.OnEnter(ctx, "player");
        }

        // Display any messages generated by the hooks
        DisplayMessages();

        await LookAsync();
    }

    private async Task<IRoom> GetCurrentRoomAsync()
    {
        var id = _state.Player!.LocationId ?? throw new InvalidOperationException("Player has no location.");
        return _state.Objects!.Get<IRoom>(id) ?? await _state.Objects.LoadAsync<IRoom>(id, _state);
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
        resettable.Reset(ctx);
        Console.WriteLine($"Reset: {objectId}");

        // Display any messages generated by the reset
        DisplayMessages();
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        await _persistence.SaveAsync(_state);
        Console.WriteLine("World state saved.");
    }

    private async Task LoadSaveAsync()
    {
        var loaded = await _persistence.LoadAsync(_state);
        if (loaded)
        {
            Console.WriteLine("World state loaded.");

            // Re-look at current location if player has one
            if (_state.Player?.LocationId is not null)
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
        // Get the instance's state store if available
        var stats = _state.Objects!.GetStats(objectId);
        IStateStore stateStore = new DictionaryStateStore();

        // Try to get the actual state store from the instance
        // For now, we create a new one since we don't expose state from ObjectManager
        // TODO: Consider exposing instance state for hook contexts

        return new MudContext
        {
            World = _state,
            State = stateStore,
            Clock = new SystemClock(),
            CurrentObjectId = objectId,
            RoomId = objectId // For rooms, their own ID is the room ID
        };
    }

    private void DisplayMessages()
    {
        var messages = _state.Messages.Drain();
        var playerRoomId = _state.Player?.LocationId;

        foreach (var msg in messages)
        {
            // Filter messages by relevance to player
            var shouldDisplay = msg.Type switch
            {
                MessageType.Tell => msg.ToId == "player",
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
                try
                {
                    var ctx = CreateContextFor(objectId);
                    heartbeat.Heartbeat(ctx);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Heartbeat error in {objectId}]: {ex.Message}");
                }
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

            try
            {
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

                method.Invoke(obj, args);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                Console.WriteLine($"[CallOut error in {callout.TargetId}.{callout.MethodName}]: {inner.Message}");
            }
        }

        // Display any messages generated by callouts
        if (dueCallouts.Count > 0)
        {
            DisplayMessages();
        }
    }
}
