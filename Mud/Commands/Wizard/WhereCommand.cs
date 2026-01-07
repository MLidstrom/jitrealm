namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Find the location of objects by id, name, or alias.
/// </summary>
public class WhereCommand : WizardCommandBase
{
    public override string Name => "where";
    public override IReadOnlyList<string> Aliases => new[] { "locate", "find" };
    public override string Usage => "where <id|name|alias>";
    public override string Description => "Find where an object is located";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var search = string.Join(" ", args).ToLowerInvariant();
        var found = new List<(string objectId, string objectName, string? containerId, string? containerName)>();

        // Search all instances
        foreach (var instanceId in context.State.Objects!.ListInstanceIds())
        {
            var obj = context.State.Objects.Get<IMudObject>(instanceId);
            if (obj is null) continue;

            bool matches = false;

            // Match by exact instance ID
            if (instanceId.ToLowerInvariant().Contains(search))
            {
                matches = true;
            }
            // Match by name
            else if (obj.Name?.ToLowerInvariant().Contains(search) == true)
            {
                matches = true;
            }
            // Match by alias
            else if (obj is IItem item && item.Aliases.Any(a => a.ToLowerInvariant().Contains(search)))
            {
                matches = true;
            }
            else if (obj is ILiving living && living.Aliases.Any(a => a.ToLowerInvariant().Contains(search)))
            {
                matches = true;
            }

            if (matches)
            {
                var containerId = context.State.Containers.GetContainer(instanceId);
                string? containerName = null;

                if (containerId is not null)
                {
                    var container = context.State.Objects.Get<IMudObject>(containerId);
                    containerName = container?.Name;
                }

                found.Add((instanceId, obj.Name ?? "(unnamed)", containerId, containerName));
            }
        }

        if (found.Count == 0)
        {
            context.Output($"No objects found matching '{search}'.");
            return Task.CompletedTask;
        }

        var lines = new List<string> { $"=== Found {found.Count} match(es) ===" };
        foreach (var (objectId, objectName, containerId, containerName) in found)
        {
            var location = containerId is not null
                ? $"{containerName ?? "unknown"} ({containerId})"
                : "(no container)";
            lines.Add($"  {objectName}");
            lines.Add($"    ID: {objectId}");
            lines.Add($"    Location: {location}");
        }

        context.Output(string.Join("\n", lines));
        return Task.CompletedTask;
    }
}
