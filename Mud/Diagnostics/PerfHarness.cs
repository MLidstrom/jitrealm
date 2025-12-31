using System.Diagnostics;
using JitRealm.Mud.Configuration;
using JitRealm.Mud.Persistence;

namespace JitRealm.Mud.Diagnostics;

/// <summary>
/// Repeatable CPU-focused driver benchmark harness.
/// Runs without networking; intended to be invoked via Program CLI args.
/// </summary>
public static class PerfHarness
{
    public sealed class Options
    {
        public string BlueprintId { get; set; } = "std/perf_dummy.cs";
        public int Count { get; set; } = 1000;
        public int Ticks { get; set; } = 2000;
        public int LoopDelayMs { get; set; } = 50;
        public bool ScheduleCallouts { get; set; } = true;
        public bool SafeInvoke { get; set; } = false;
    }

    public static Options ParseArgs(string[] args, DriverSettings settings)
    {
        var opt = new Options
        {
            BlueprintId = "std/perf_dummy.cs",
            LoopDelayMs = settings.GameLoop.LoopDelayMs
        };

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--blueprint":
                    if (i + 1 < args.Length) opt.BlueprintId = args[++i];
                    break;
                case "--count":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var c)) opt.Count = c;
                    break;
                case "--ticks":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var t)) opt.Ticks = t;
                    break;
                case "--loopDelayMs":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var d)) opt.LoopDelayMs = d;
                    break;
                case "--noCallouts":
                    opt.ScheduleCallouts = false;
                    break;
                case "--safeInvoke":
                    opt.SafeInvoke = true;
                    break;
            }
        }

        return opt;
    }

    public static async Task<int> RunAsync(string baseDir, DriverSettings settings, string[] args)
    {
        var options = ParseArgs(args, settings);

        Console.WriteLine("=== JitRealm perfbench ===");
        Console.WriteLine($"Blueprint: {options.BlueprintId}");
        Console.WriteLine($"Count:     {options.Count}");
        Console.WriteLine($"Ticks:     {options.Ticks}");
        Console.WriteLine($"Tick dt:   {options.LoopDelayMs} ms");
        Console.WriteLine($"Callouts:  {(options.ScheduleCallouts ? "on" : "off")}");
        Console.WriteLine($"SafeInvoke:{(options.SafeInvoke ? "on" : "off")}");
        Console.WriteLine();

        var worldDir = Path.Combine(baseDir, settings.Paths.WorldDirectory);
        var clock = new ManualClock();
        var state = new WorldState(clock)
        {
            Objects = new ObjectManager(
                worldDir,
                clock,
                forceGcOnUnload: settings.Performance.ForceGcOnUnload,
                forceGcEveryNUnloads: settings.Performance.ForceGcEveryNUnloads)
        };

        // Persistence is unused here, but some code paths expect it; keep it inert.
        _ = new WorldStatePersistence(new JsonPersistenceProvider(Path.Combine(baseDir, settings.Paths.SaveDirectory, settings.Paths.SaveFileName)));

        // Ensure a room exists so we can place objects (optional but keeps ContainerRegistry exercised)
        var startRoom = await state.Objects.LoadAsync<IRoom>(settings.Paths.StartRoom, state);

        // Clone N objects and put them in the start room
        var sw = Stopwatch.StartNew();
        var created = new List<string>(capacity: options.Count);

        for (int i = 0; i < options.Count; i++)
        {
            var obj = await state.Objects.CloneAsync<IMudObject>(options.BlueprintId, state);
            created.Add(obj.Id);
            state.Containers.Add(startRoom.Id, obj.Id);

            // Optionally schedule a repeating callout on each object
            if (options.ScheduleCallouts)
            {
                // Method name is part of the perf dummy contract.
                state.CallOuts.ScheduleEvery(obj.Id, "Tick", TimeSpan.FromSeconds(1), args: null);
            }
        }

        sw.Stop();
        Console.WriteLine($"Created {created.Count} instances in {sw.Elapsed.TotalMilliseconds:F1} ms");
        Console.WriteLine($"Heartbeat registrations: {state.Heartbeats.Count}");
        Console.WriteLine($"Pending callouts: {state.CallOuts.Count}");
        Console.WriteLine();

        // Run a deterministic tick loop: advance time; process due heartbeats/callouts.
        long hbTicksTotal = 0;
        long coTicksTotal = 0;
        int hbDueTotal = 0;
        int coDueTotal = 0;

        var dt = TimeSpan.FromMilliseconds(options.LoopDelayMs);
        var total = Stopwatch.StartNew();

        for (int tick = 0; tick < options.Ticks; tick++)
        {
            clock.Advance(dt);

            var hbStart = Stopwatch.GetTimestamp();
            var dueHeartbeats = state.Heartbeats.GetDueHeartbeats();
            hbTicksTotal += Stopwatch.GetTimestamp() - hbStart;
            hbDueTotal += dueHeartbeats.Count;

            if (options.SafeInvoke)
            {
                foreach (var id in dueHeartbeats)
                {
                    var obj = state.Objects.Get<IMudObject>(id);
                    if (obj is IHeartbeat hb)
                    {
                        var ctx = state.CreateContext(id);
                        JitRealm.Mud.Security.SafeInvoker.TryInvokeHeartbeat(() => hb.Heartbeat(ctx), $"Heartbeat in {id}");
                    }
                }
            }
            else
            {
                foreach (var id in dueHeartbeats)
                {
                    var obj = state.Objects.Get<IMudObject>(id);
                    if (obj is IHeartbeat hb)
                    {
                        var ctx = state.CreateContext(id);
                        hb.Heartbeat(ctx);
                    }
                }
            }

            var coStart = Stopwatch.GetTimestamp();
            var dueCallouts = state.CallOuts.GetDueCallouts();
            coTicksTotal += Stopwatch.GetTimestamp() - coStart;
            coDueTotal += dueCallouts.Count;

            if (dueCallouts.Count > 0)
            {
                foreach (var callout in dueCallouts)
                {
                    var obj = state.Objects.Get<IMudObject>(callout.TargetId);
                    if (obj is null) continue;

                    var method = obj.GetType().GetMethod(callout.MethodName);
                    if (method is null) continue;

                    var ctx = state.CreateContext(callout.TargetId);
                    var parameters = method.GetParameters();
                    var invokeArgs = new object?[parameters.Length];

                    if (parameters.Length > 0 && parameters[0].ParameterType == typeof(IMudContext))
                    {
                        invokeArgs[0] = ctx;
                        if (callout.Args is not null)
                        {
                            for (int i = 1; i < parameters.Length && i - 1 < callout.Args.Length; i++)
                                invokeArgs[i] = callout.Args[i - 1];
                        }
                    }
                    else if (callout.Args is not null)
                    {
                        for (int i = 0; i < parameters.Length && i < callout.Args.Length; i++)
                            invokeArgs[i] = callout.Args[i];
                    }

                    if (options.SafeInvoke)
                    {
                        JitRealm.Mud.Security.SafeInvoker.TryInvokeCallout(
                            () => method.Invoke(obj, invokeArgs),
                            $"CallOut {callout.MethodName} in {callout.TargetId}");
                    }
                    else
                    {
                        method.Invoke(obj, invokeArgs);
                    }
                }
            }
        }

        total.Stop();

        Console.WriteLine("=== Results ===");
        Console.WriteLine($"Total runtime: {total.Elapsed.TotalMilliseconds:F1} ms");
        Console.WriteLine($"Heartbeat scheduler: {LoopMetrics.TicksToMs(hbTicksTotal):F1} ms, due total {hbDueTotal}");
        Console.WriteLine($"Callout scheduler:   {LoopMetrics.TicksToMs(coTicksTotal):F1} ms, due total {coDueTotal}");

        return 0;
    }
}


