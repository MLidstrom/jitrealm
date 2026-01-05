using JitRealm.Mud;
using JitRealm.Mud.AI;
using JitRealm.Mud.Configuration;
using JitRealm.Mud.Diagnostics;
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
var perfBenchMode = args.Contains("--perfbench");
string? autoPlayer = null;
string? autoPassword = null;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out var p))
    {
        settings.Server.Port = p;
    }
    else if (args[i] is "--player" or "-u")
    {
        autoPlayer = args[i + 1];
    }
    else if (args[i] is "--password" or "-pw")
    {
        autoPassword = args[i + 1];
    }
}

var worldDir = Path.Combine(baseDir, settings.Paths.WorldDirectory);
var saveDir = Path.Combine(baseDir, settings.Paths.SaveDirectory);
var savePath = Path.Combine(saveDir, settings.Paths.SaveFileName);

var state = new WorldState
{
    Objects = new ObjectManager(
        worldDir,
        new SystemClock(),
        forceGcOnUnload: settings.Performance.ForceGcOnUnload,
        forceGcEveryNUnloads: settings.Performance.ForceGcEveryNUnloads)
};

// Set up LLM service if enabled
if (settings.Llm.Enabled)
{
    var llmService = new OllamaLlmService(settings.Llm);
    state.LlmService = llmService;
    Console.WriteLine($"LLM service enabled: {settings.Llm.Provider} ({settings.Llm.Model})");
}

// Set up persistence
var provider = new JsonPersistenceProvider(savePath);
var persistence = new WorldStatePersistence(provider);

if (perfBenchMode)
{
    // Run a deterministic driver benchmark and exit.
    // Example:
    //   dotnet run -- --perfbench --blueprint std/perf_dummy.cs --count 2000 --ticks 5000
    await PerfHarness.RunAsync(baseDir, settings, args);
    return;
}

if (serverMode)
{
    // Multi-player server mode
    var server = new GameServer(state, persistence, settings);

    // Handle Ctrl+C gracefully with force-quit on second press
    using var cts = new CancellationTokenSource();
    var ctrlCCount = 0;

    Console.CancelKeyPress += (_, e) =>
    {
        ctrlCCount++;
        if (ctrlCCount == 1)
        {
            // First Ctrl+C: try graceful shutdown
            Console.WriteLine("\nShutting down... (press Ctrl+C again to force quit)");
            e.Cancel = true;
            server.Stop();
            cts.Cancel();
        }
        else
        {
            // Second Ctrl+C: force quit
            Console.WriteLine("\nForce quitting...");
            e.Cancel = false; // Allow default termination
        }
    };

    await server.RunAsync(cts.Token);
}
else
{
    // Single-player console mode
    // CommandLoop now handles player creation as a world object
    var loop = new CommandLoop(state, persistence, settings, autoPlayer, autoPassword);
    await loop.RunAsync();
}
