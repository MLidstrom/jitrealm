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
