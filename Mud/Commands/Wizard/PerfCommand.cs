using JitRealm.Mud.Diagnostics;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Show driver performance metrics (last-tick timings and basic counts).
/// </summary>
public sealed class PerfCommand : WizardCommandBase
{
    public override string Name => "perf";
    public override string Usage => "perf";
    public override string Description => "Show driver loop timings and scheduler stats";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var state = context.State;
        var objects = state.Objects;

        var lines = new List<string>
        {
            "=== Perf (last tick) ===",
            $"Tick#: {state.Metrics.TickCount}",
            $"Tick: {state.Metrics.TickLastMs:F3} ms",
        };

        var hb = state.Metrics.HeartbeatsLast;
        var co = state.Metrics.CalloutsLast;
        lines.Add($"Heartbeats: {hb.ms:F3} ms (due {hb.due}, registered {state.Heartbeats.Count})");
        lines.Add($"Callouts:   {co.ms:F3} ms (due {co.due}, pending {state.CallOuts.Count})");
        lines.Add($"Combat:    {state.Metrics.CombatLastMs:F3} ms");
        lines.Add($"Input:     {state.Metrics.InputLastMs:F3} ms");
        lines.Add($"Deliver:   {state.Metrics.DeliverLastMs:F3} ms");

        if (objects is not null)
        {
            // These enumerate/sort today; we'll optimize later, but keep the output useful now.
            lines.Add("");
            lines.Add("=== World ===");
            lines.Add($"Blueprints loaded: {objects.BlueprintCount}");
            lines.Add($"Instances loaded:  {objects.InstanceCount}");
        }

        context.Output(string.Join("\n", lines));
        return Task.CompletedTask;
    }
}


