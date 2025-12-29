using JitRealm.Mud.Persistence;

namespace JitRealm.Mud.Network;

/// <summary>
/// Main game server handling multi-player connections.
/// </summary>
public sealed class GameServer
{
    private readonly WorldState _state;
    private readonly WorldStatePersistence _persistence;
    private readonly TelnetServer _telnet;
    private readonly string _startRoomId;
    private bool _running;

    public GameServer(WorldState state, WorldStatePersistence persistence, int port = 4000, string startRoomId = "Rooms/start.cs")
    {
        _state = state;
        _persistence = persistence;
        _startRoomId = startRoomId;
        _telnet = new TelnetServer(port);

        _telnet.OnClientConnected += OnClientConnected;
    }

    public int Port => _telnet.Port;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _telnet.Start();
        _running = true;

        Console.WriteLine($"JitRealm v0.6 - Multi-user server");
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

                // Process input from all sessions
                await ProcessAllSessionsAsync();

                // Deliver messages to sessions
                DeliverMessages();

                // Prune disconnected sessions
                _state.Sessions.PruneDisconnected();

                // Small delay to prevent busy-loop
                await Task.Delay(50, cancellationToken);
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

        // Create player for this session
        var playerName = $"Player{_state.Sessions.Count + 1}";
        var player = new Player(playerName);

        // Load start room and set player location
        try
        {
            var startRoom = await _state.Objects!.LoadAsync<IRoom>(_startRoomId, _state);
            player.LocationId = startRoom.Id;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{session.SessionId}] Failed to load start room: {ex.Message}");
            await session.CloseAsync();
            return;
        }

        session.Player = player;
        _state.Sessions.Add(session);

        // Welcome message
        await session.WriteLineAsync($"Welcome to JitRealm, {playerName}!");
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
        var player = session.Player!;

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
                BroadcastToRoom(player.LocationId!, $"{player.Name} says: {sayMsg}", session);
                await session.WriteLineAsync($"You say: {sayMsg}");
                break;

            case "who":
                await ShowWhoAsync(session);
                break;

            case "help":
                await session.WriteLineAsync("Commands: look, go <exit>, say <msg>, who, quit");
                break;

            case "quit":
            case "exit":
                Console.WriteLine($"[{session.SessionId}] {player.Name} disconnected");
                BroadcastToRoom(player.LocationId!, $"{player.Name} has left the realm.", session);
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
        var player = session.Player!;
        var room = _state.Objects!.Get<IRoom>(player.LocationId!);
        if (room is null)
        {
            await session.WriteLineAsync("You are nowhere.");
            return;
        }

        await session.WriteLineAsync(room.Name);
        await session.WriteLineAsync(room.Description);

        if (room.Exits.Count > 0)
            await session.WriteLineAsync("Exits: " + string.Join(", ", room.Exits.Keys));

        // Show other players in room
        var othersHere = _state.Sessions.GetSessionsInRoom(player.LocationId!)
            .Where(s => s != session && s.Player is not null)
            .Select(s => s.Player!.Name)
            .ToList();

        if (othersHere.Count > 0)
            await session.WriteLineAsync("Players here: " + string.Join(", ", othersHere));
    }

    private async Task GoAsync(ISession session, string exit)
    {
        var player = session.Player!;
        var currentRoom = _state.Objects!.Get<IRoom>(player.LocationId!);
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
        BroadcastToRoom(player.LocationId!, $"{player.Name} leaves {exit}.", session);

        // Move to destination
        var destRoom = await _state.Objects.LoadAsync<IRoom>(destId, _state);
        player.LocationId = destRoom.Id;

        // Notify others in new room
        BroadcastToRoom(player.LocationId!, $"{player.Name} has arrived.", session);

        // Show new room
        await ShowRoomAsync(session);
    }

    private async Task ShowWhoAsync(ISession session)
    {
        var sessions = _state.Sessions.GetAll();
        await session.WriteLineAsync($"Players online: {sessions.Count}");
        foreach (var s in sessions)
        {
            if (s.Player is not null)
            {
                var location = _state.Objects!.Get<IRoom>(s.Player.LocationId ?? "")?.Name ?? "unknown";
                await session.WriteLineAsync($"  {s.Player.Name} - {location}");
            }
        }
    }

    private void BroadcastToRoom(string roomId, string message, ISession? exclude = null)
    {
        foreach (var session in _state.Sessions.GetSessionsInRoom(roomId))
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
    }

    private void ProcessCallOuts()
    {
        var dueCallouts = _state.CallOuts.GetDueCallouts();

        foreach (var callout in dueCallouts)
        {
            var obj = _state.Objects!.Get<IMudObject>(callout.TargetId);
            if (obj is null) continue;

            try
            {
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

                method.Invoke(obj, args);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                Console.WriteLine($"[CallOut error in {callout.TargetId}.{callout.MethodName}]: {inner.Message}");
            }
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
                    var targetSession = _state.Sessions.GetByPlayerId(msg.ToId ?? "");
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

                        foreach (var session in _state.Sessions.GetSessionsInRoom(msg.RoomId))
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
        return new MudContext
        {
            World = _state,
            State = new DictionaryStateStore(),
            Clock = new SystemClock(),
            CurrentObjectId = objectId,
            RoomId = objectId
        };
    }

    private string GetObjectName(string objectId)
    {
        var obj = _state.Objects!.Get<IMudObject>(objectId);
        return obj?.Name ?? objectId;
    }
}
