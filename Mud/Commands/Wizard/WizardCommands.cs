namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Base class for wizard-only commands.
/// </summary>
public abstract class WizardCommandBase : CommandBase
{
    public override bool IsWizardOnly => true;
    public override string Category => "Wizard";
}

/// <summary>
/// List all loaded blueprints.
/// </summary>
public class BlueprintsCommand : WizardCommandBase
{
    public override string Name => "blueprints";
    public override string Usage => "blueprints";
    public override string Description => "List all loaded blueprints";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var lines = new List<string> { "=== Blueprints ===" };
        foreach (var id in context.State.Objects!.ListBlueprintIds())
        {
            lines.Add($"  {id}");
        }
        context.Output(string.Join("\n", lines));
        return Task.CompletedTask;
    }
}

/// <summary>
/// List all loaded object instances.
/// </summary>
public class ObjectsCommand : WizardCommandBase
{
    public override string Name => "objects";
    public override string Usage => "objects";
    public override string Description => "List all object instances";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var lines = new List<string> { "=== Instances ===" };
        foreach (var id in context.State.Objects!.ListInstanceIds())
        {
            lines.Add($"  {id}");
        }
        context.Output(string.Join("\n", lines));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Reload a blueprint from disk.
/// </summary>
public class ReloadCommand : WizardCommandBase
{
    public override string Name => "reload";
    public override string Usage => "reload <blueprintId>";
    public override string Description => "Hot-reload a blueprint from disk";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var blueprintId = args[0];
        await context.State.Objects!.ReloadBlueprintAsync(blueprintId, context.State);
        context.Output($"Reloaded blueprint {blueprintId}");
    }
}

/// <summary>
/// Unload a blueprint and all its instances.
/// </summary>
public class UnloadCommand : WizardCommandBase
{
    public override string Name => "unload";
    public override string Usage => "unload <blueprintId>";
    public override string Description => "Unload a blueprint and its instances";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var blueprintId = args[0];
        await context.State.Objects!.UnloadBlueprintAsync(blueprintId, context.State);
        context.Output($"Unloaded blueprint {blueprintId}");
    }
}

/// <summary>
/// Clone a blueprint to create a new instance.
/// </summary>
public class CloneCommand : WizardCommandBase
{
    public override string Name => "clone";
    public override string Usage => "clone <blueprintId>";
    public override string Description => "Create a new instance from a blueprint";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var blueprintId = args[0];
        var cloned = await context.State.Objects!.CloneAsync<IMudObject>(blueprintId, context.State);
        var playerLocation = context.GetPlayerLocation();

        if (cloned is not null && playerLocation is not null)
        {
            context.State.Containers.Add(playerLocation, cloned.Id);
            context.Output($"Cloned {blueprintId} -> {cloned.Id}");
        }
        else
        {
            context.Output($"Failed to clone {blueprintId}");
        }
    }
}

/// <summary>
/// Destruct (remove) an object instance.
/// </summary>
public class DestructCommand : WizardCommandBase
{
    public override string Name => "destruct";
    public override string Usage => "destruct <objectId>";
    public override string Description => "Remove an object instance";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var objectId = args[0];
        await context.State.Objects!.DestructAsync(objectId, context.State);
        context.State.Containers.Remove(objectId);
        context.Output($"Destructed {objectId}");
    }
}

/// <summary>
/// Show detailed information about a blueprint or instance.
/// </summary>
public class StatCommand : WizardCommandBase
{
    public override string Name => "stat";
    public override string Usage => "stat <id>";
    public override string Description => "Show details about a blueprint or instance";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var id = args[0];
        var stats = context.State.Objects!.GetStats(id);

        if (stats is null)
        {
            context.Output($"Object not found: {id}");
        }
        else
        {
            var lines = new List<string>
            {
                $"=== {(stats.IsBlueprint ? "Blueprint" : "Instance")}: {stats.Id} ===",
                $"  Type: {stats.TypeName}",
                $"  Blueprint: {stats.BlueprintId}"
            };

            if (stats.IsBlueprint)
            {
                lines.Add($"  Instances: {stats.InstanceCount}");
                lines.Add($"  Source Modified: {stats.SourceMtime}");
            }
            else
            {
                lines.Add($"  Created: {stats.CreatedAt}");
                if (stats.StateKeys?.Length > 0)
                {
                    lines.Add($"  State Keys: {string.Join(", ", stats.StateKeys)}");
                }
            }

            context.Output(string.Join("\n", lines));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Trigger reset on a resettable object.
/// </summary>
public class ResetCommand : WizardCommandBase
{
    public override string Name => "reset";
    public override string Usage => "reset <objectId>";
    public override string Description => "Trigger reset on a resettable object";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var objectId = args[0];
        var obj = context.State.Objects!.Get<IMudObject>(objectId);

        if (obj is null)
        {
            context.Output($"Object not found: {objectId}");
        }
        else if (obj is IResettable resettable)
        {
            var ctx = context.CreateContext(objectId);
            resettable.Reset(ctx);
            context.Output($"Reset {objectId}");
        }
        else
        {
            context.Output($"Object is not resettable: {objectId}");
        }

        return Task.CompletedTask;
    }
}
