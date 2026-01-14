namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to trace NPC AI decisions in real-time.
/// </summary>
public class TraceCommand : WizardCommandBase
{
    public override string Name => "trace";
    public override string[] Aliases => new[] { "tr" };
    public override string Usage => "trace [<npc>|off [<npc>]]";
    public override string Description => "Watch NPC AI decisions in real-time";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var session = context.Session;
        if (session is null)
        {
            context.Output("Trace requires a session.");
            return Task.CompletedTask;
        }

        var tracer = context.State.NpcTracer;

        // No args: show currently traced NPCs
        if (args.Length == 0)
        {
            var traced = tracer.GetTracedNpcs(session.SessionId);
            if (traced.Count == 0)
            {
                context.Output("Not tracing any NPCs.");
                context.Output("Usage: trace <npc> - start tracing");
                context.Output("       trace off   - stop all traces");
            }
            else
            {
                context.Output("Currently tracing:");
                foreach (var npcId in traced)
                {
                    context.Output($"  {npcId}");
                }
            }
            return Task.CompletedTask;
        }

        // "trace off" or "trace off <npc>"
        if (string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length == 1)
            {
                // Stop all traces
                var traced = tracer.GetTracedNpcs(session.SessionId);
                tracer.StopAllTraces(session.SessionId);
                context.Output($"Stopped tracing {traced.Count} NPC(s).");
            }
            else
            {
                // Stop tracing specific NPC
                var npcId = ResolveNpc(context, args[1]);
                if (npcId is not null)
                {
                    tracer.StopTrace(session.SessionId, npcId);
                    context.Output($"Stopped tracing {npcId}");
                }
            }
            return Task.CompletedTask;
        }

        // "trace <npc>" - start tracing
        var targetNpcId = ResolveNpc(context, args[0]);
        if (targetNpcId is null)
            return Task.CompletedTask;

        // Check if already tracing
        var currentlyTraced = tracer.GetTracedNpcs(session.SessionId);
        if (currentlyTraced.Contains(targetNpcId))
        {
            context.Output($"Already tracing {targetNpcId}");
            return Task.CompletedTask;
        }

        tracer.StartTrace(session.SessionId, targetNpcId);
        context.Output($"Now tracing: {targetNpcId}");
        context.Output("Trace events will appear as [TRACE ...] messages.");
        context.Output("Use 'trace off' to stop.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolve NPC by name, alias, or ID.
    /// </summary>
    private string? ResolveNpc(CommandContext context, string nameOrId)
    {
        // First try direct ID match
        var obj = context.State.Objects?.Get<ILiving>(nameOrId);
        if (obj is not null)
            return nameOrId;

        // Search by name or alias in current room
        var roomId = context.GetPlayerLocation();
        if (roomId is not null)
        {
            var contents = context.State.Containers.GetContents(roomId);
            foreach (var itemId in contents)
            {
                var living = context.State.Objects?.Get<ILiving>(itemId);
                if (living is null)
                    continue;

                // Check name
                if (living.Name?.Contains(nameOrId, StringComparison.OrdinalIgnoreCase) == true)
                    return itemId;

                // Check aliases
                if (living.Aliases.Any(a => a.Contains(nameOrId, StringComparison.OrdinalIgnoreCase)))
                    return itemId;
            }
        }

        // Search globally by name/alias
        foreach (var instanceId in context.State.Objects?.ListInstanceIds() ?? Array.Empty<string>())
        {
            var living = context.State.Objects?.Get<ILiving>(instanceId);
            if (living is null)
                continue;

            // Check name (exact match for global)
            if (string.Equals(living.Name, nameOrId, StringComparison.OrdinalIgnoreCase))
                return instanceId;

            // Check aliases (exact match for global)
            if (living.Aliases.Any(a => string.Equals(a, nameOrId, StringComparison.OrdinalIgnoreCase)))
                return instanceId;

            // Check if ID contains the search term
            if (instanceId.Contains(nameOrId, StringComparison.OrdinalIgnoreCase))
                return instanceId;
        }

        context.Output($"NPC not found: {nameOrId}");
        context.Output("Use 'where <name>' to find NPCs, or use the full instance ID.");
        return null;
    }
}
