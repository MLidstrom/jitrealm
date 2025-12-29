namespace JitRealm.Mud;

public sealed class CommandLoop
{
    private readonly WorldState _state;

    public CommandLoop(WorldState state) => _state = state;

    public async Task RunAsync()
    {
        Console.WriteLine("JitRealm v0.2");
        Console.WriteLine("Commands: look, go <exit>, objects, blueprints, clone <bp>, destruct <id>, stat <id>, reload <bp>, unload <bp>, quit");

        while (true)
        {
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
                        await _state.Objects!.UnloadBlueprintAsync(parts[1]);
                        Console.WriteLine($"Unloaded blueprint {parts[1]}");
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

        await _state.Objects!.DestructAsync(objectId);
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
        var room = await GetCurrentRoomAsync();
        if (!room.Exits.TryGetValue(exit, out var destId))
        {
            Console.WriteLine("You can't go that way.");
            return;
        }

        var dest = await _state.Objects!.LoadAsync<IRoom>(destId, _state);
        _state.Player!.LocationId = dest.Id;

        await LookAsync();
    }

    private async Task<IRoom> GetCurrentRoomAsync()
    {
        var id = _state.Player!.LocationId ?? throw new InvalidOperationException("Player has no location.");
        return _state.Objects!.Get<IRoom>(id) ?? await _state.Objects.LoadAsync<IRoom>(id, _state);
    }
}
