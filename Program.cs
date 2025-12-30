using JitRealm.Mud;
using JitRealm.Mud.Network;
using JitRealm.Mud.Persistence;

// Parse command-line arguments
var serverMode = args.Contains("--server") || args.Contains("-s");
var port = 4000;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out var p))
    {
        port = p;
        break;
    }
}

var baseDir = AppContext.BaseDirectory;
var worldDir = Path.Combine(baseDir, "World");
var saveDir = Path.Combine(baseDir, "save");
var savePath = Path.Combine(saveDir, "world.json");

var state = new WorldState
{
    Objects = new ObjectManager(worldDir, new SystemClock())
};

// Set up persistence
var provider = new JsonPersistenceProvider(savePath);
var persistence = new WorldStatePersistence(provider);

if (serverMode)
{
    // Multi-player server mode
    var server = new GameServer(state, persistence, port);

    // Handle Ctrl+C gracefully
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        server.Stop();
        cts.Cancel();
    };

    await server.RunAsync(cts.Token);
}
else
{
    // Single-player console mode
    // CommandLoop now handles player creation as a world object
    var loop = new CommandLoop(state, persistence);
    await loop.RunAsync();
}
