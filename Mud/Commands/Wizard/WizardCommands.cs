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
    public override string Usage => "reload <blueprintId|here>";
    public override string Description => "Hot-reload a blueprint from disk";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var blueprintId = context.ResolveObjectId(args[0]);
        if (blueprintId is null)
        {
            context.Output("Could not resolve object reference.");
            return;
        }

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
    public override string Usage => "unload <blueprintId|here>";
    public override string Description => "Unload a blueprint and its instances";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var blueprintId = context.ResolveObjectId(args[0]);
        if (blueprintId is null)
        {
            context.Output("Could not resolve object reference.");
            return;
        }

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
    public override string Usage => "clone <blueprintId> [to <destination>]";
    public override string Description => "Create a new instance from a blueprint";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var blueprintId = args[0];

        // Parse optional "to <destination>"
        string? destinationId = null;
        if (args.Length >= 3 && args[1].Equals("to", StringComparison.OrdinalIgnoreCase))
        {
            var destRef = string.Join(" ", args.Skip(2));

            if (destRef.Equals("me", StringComparison.OrdinalIgnoreCase) ||
                destRef.Equals("self", StringComparison.OrdinalIgnoreCase))
            {
                destinationId = context.PlayerId;
            }
            else if (destRef.Equals("here", StringComparison.OrdinalIgnoreCase))
            {
                destinationId = context.GetPlayerLocation();
            }
            else
            {
                destinationId = context.ResolveObjectId(destRef);

                // Try finding NPC/container in room by name
                if (destinationId is null)
                {
                    var roomId = context.GetPlayerLocation();
                    if (roomId is not null)
                    {
                        destinationId = FindDestinationInRoom(context, destRef.ToLowerInvariant(), roomId);
                    }
                }

                // Try loading as room blueprint
                if (destinationId is null)
                {
                    var roomBlueprintId = destRef.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        ? destRef
                        : destRef + ".cs";
                    try
                    {
                        var room = await context.State.Objects!.LoadAsync<IRoom>(roomBlueprintId, context.State);
                        destinationId = room?.Id;
                    }
                    catch
                    {
                        // Not a valid room
                    }
                }
            }

            if (destinationId is null)
            {
                context.Output($"Cannot find destination: {destRef}");
                return;
            }
        }

        // Default destination is player's current location
        destinationId ??= context.GetPlayerLocation();

        var cloned = await context.State.Objects!.CloneAsync<IMudObject>(blueprintId, context.State);

        if (cloned is not null && destinationId is not null)
        {
            context.State.Containers.Add(destinationId, cloned.Id);

            // Get destination name for output
            var destObj = context.State.Objects?.Get<IMudObject>(destinationId);
            var destName = destObj?.Name ?? destinationId;

            context.Output($"Cloned {blueprintId} -> {cloned.Id} (in {destName})");
        }
        else
        {
            context.Output($"Failed to clone {blueprintId}");
        }
    }

    private static string? FindDestinationInRoom(CommandContext context, string name, string roomId)
    {
        var contents = context.State.Containers.GetContents(roomId);
        foreach (var objId in contents)
        {
            var obj = context.State.Objects?.Get<IMudObject>(objId);
            if (obj is null)
                continue;

            // Only match livings or containers
            if (obj is not ILiving && obj is not IContainer)
                continue;

            if (obj.Name.ToLowerInvariant().Contains(name))
                return objId;

            if (obj is ILiving living)
            {
                foreach (var alias in living.Aliases)
                {
                    if (alias.ToLowerInvariant().Contains(name))
                        return objId;
                }
            }
        }
        return null;
    }
}

/// <summary>
/// Destruct (remove) an object instance.
/// </summary>
public class DestructCommand : WizardCommandBase
{
    public override string Name => "destruct";
    public override string Usage => "destruct <objectId|here>";
    public override string Description => "Remove an object instance";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var objectId = context.ResolveObjectId(args[0]);
        if (objectId is null)
        {
            context.Output("Could not resolve object reference.");
            return;
        }

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
    public override string Usage => "stat <id|here>";
    public override string Description => "Show details about a blueprint or instance";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var id = context.ResolveObjectId(args[0]);
        if (id is null)
        {
            context.Output("Could not resolve object reference.");
            return Task.CompletedTask;
        }

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
    public override string Usage => "reset <objectId|here>";
    public override string Description => "Trigger reset on a resettable object";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var objectId = context.ResolveObjectId(args[0]);
        if (objectId is null)
        {
            context.Output("Could not resolve object reference.");
            return Task.CompletedTask;
        }

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

/// <summary>
/// Modify state variables on a loaded object at runtime.
/// </summary>
public class PatchCommand : WizardCommandBase
{
    public override string Name => "patch";
    public override string Usage => "patch <objectId|here> [key] [value]";
    public override string Description => "View or modify object state variables";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.Output("Usage: patch <objectId|here> [key] [value]");
            context.Output("  patch <objectId>           - show all state variables");
            context.Output("  patch <objectId> <key>     - show specific variable");
            context.Output("  patch <objectId> <key> <value> - set variable");
            context.Output("  Use 'here' to reference current room");
            context.Output("Value types: int (123), bool (true/false), string (anything else)");
            return Task.CompletedTask;
        }

        var objectId = context.ResolveObjectId(args[0]);
        if (objectId is null)
        {
            context.Output("Could not resolve object reference.");
            return Task.CompletedTask;
        }

        var stateStore = context.State.Objects!.GetStateStore(objectId);

        if (stateStore is null)
        {
            context.Output($"Object not found: {objectId}");
            return Task.CompletedTask;
        }

        // Show all state if no key specified
        if (args.Length == 1)
        {
            var keys = stateStore.Keys;
            if (!keys.Any())
            {
                context.Output($"Object {objectId} has no state variables.");
                return Task.CompletedTask;
            }

            var lines = new List<string> { $"=== State for {objectId} ===" };
            foreach (var key in keys.OrderBy(k => k))
            {
                var value = stateStore.Get<object>(key);
                var typeName = value?.GetType().Name ?? "null";
                lines.Add($"  {key} ({typeName}): {value}");
            }
            context.Output(string.Join("\n", lines));
            return Task.CompletedTask;
        }

        var stateKey = args[1];

        // Show specific key if no value specified
        if (args.Length == 2)
        {
            if (!stateStore.Has(stateKey))
            {
                context.Output($"Key '{stateKey}' not found on {objectId}");
                return Task.CompletedTask;
            }

            var value = stateStore.Get<object>(stateKey);
            var typeName = value?.GetType().Name ?? "null";
            context.Output($"{stateKey} ({typeName}): {value}");
            return Task.CompletedTask;
        }

        // Set value - join remaining args for values with spaces
        var valueStr = string.Join(" ", args.Skip(2));

        // Type inference
        object parsedValue;
        if (int.TryParse(valueStr, out var intVal))
        {
            parsedValue = intVal;
        }
        else if (double.TryParse(valueStr, out var doubleVal) && valueStr.Contains('.'))
        {
            parsedValue = doubleVal;
        }
        else if (bool.TryParse(valueStr, out var boolVal))
        {
            parsedValue = boolVal;
        }
        else
        {
            parsedValue = valueStr;
        }

        stateStore.Set(stateKey, parsedValue);
        context.Output($"Set {objectId}.{stateKey} = {parsedValue} ({parsedValue.GetType().Name})");

        return Task.CompletedTask;
    }
}
