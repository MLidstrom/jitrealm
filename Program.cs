using JitRealm.Mud;
using JitRealm.Mud.Configuration;
using JitRealm.Mud.Network;
using JitRealm.Mud.Persistence;
using Microsoft.Extensions.Configuration;

// Load configuration from appsettings.json
var baseDir = AppContext.BaseDirectory;
var configPath = Path.Combine(baseDir, "appsettings.json");

var configuration = new ConfigurationBuilder()
    .SetBasePath(baseDir)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

var settings = new DriverSettings();
configuration.Bind(settings);

// Parse command-line arguments (override settings)
var serverMode = args.Contains("--server") || args.Contains("-s");
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out var p))
    {
        settings.Server.Port = p;
        break;
    }
}

var worldDir = Path.Combine(baseDir, settings.Paths.WorldDirectory);
var saveDir = Path.Combine(baseDir, settings.Paths.SaveDirectory);
var savePath = Path.Combine(saveDir, settings.Paths.SaveFileName);

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
    var server = new GameServer(state, persistence, settings);

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
    var loop = new CommandLoop(state, persistence, settings);
    await loop.RunAsync();
}
