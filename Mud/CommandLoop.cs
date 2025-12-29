namespace JitRealm.Mud;

public sealed class CommandLoop
{
    private readonly WorldState _state;

    public CommandLoop(WorldState state) => _state = state;

    public async Task RunAsync()
    {
        Console.WriteLine("JitRealm — commands: look, go <exit>, objects, reload <id>, unload <id>, quit");

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
                        foreach (var id in _state.Objects!.ListLoadedIds())
                            Console.WriteLine(id);
                        break;

                    case "reload":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: reload <objectId>");
                            break;
                        }
                        await _state.Objects!.ReloadAsync<IMudObject>(parts[1], _state);
                        Console.WriteLine($"Reloaded {parts[1]}");
                        break;

                    case "unload":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: unload <objectId>");
                            break;
                        }
                        await _state.Objects!.UnloadAsync(parts[1]);
                        Console.WriteLine($"Unloaded {parts[1]}");
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
                Console.WriteLine(ex.Message);
            }
        }
    }

    private async Task LookAsync()
    {
        var room = await GetCurrentRoomAsync();
        Console.WriteLine(room.Name);
        Console.WriteLine(room.Description);

        if (room.Exits.Count > 0)
            Console.WriteLine("Exits: " + string.Join(", ", room.Exits.Keys));

        if (room.Contents.Count > 0)
            Console.WriteLine("You see: " + string.Join(", ", room.Contents));
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
