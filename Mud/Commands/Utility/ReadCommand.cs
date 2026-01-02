namespace JitRealm.Mud.Commands.Utility;

/// <summary>
/// Read a sign, book, scroll, or other readable object.
/// </summary>
public class ReadCommand : CommandBase
{
    public override string Name => "read";
    public override IReadOnlyList<string> Aliases => Array.Empty<string>();
    public override string Usage => "read <object>";
    public override string Description => "Read a sign, book, or other readable object";
    public override string Category => "Utility";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1))
            return Task.CompletedTask;

        var target = JoinArgs(args);
        var playerId = context.PlayerId;
        var roomId = context.State.Containers.GetContainer(playerId);

        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return Task.CompletedTask;
        }

        // First check the room itself (for signs/items defined as room properties)
        var room = context.State.Objects?.Get<IRoom>(roomId);

        // Look for readable objects in the room
        var roomContents = context.State.Containers.GetContents(roomId);
        IReadable? readable = null;
        string? readableId = null;

        foreach (var objId in roomContents)
        {
            var obj = context.State.Objects?.Get<IReadable>(objId);
            if (obj is null) continue;

            // Check if the object name or aliases match the target
            if (MatchesTarget(obj, target))
            {
                readable = obj;
                readableId = objId;
                break;
            }
        }

        // Also check the room's Details dictionary for simple text reads
        if (readable is null && room is IMudObject mudObj && mudObj.Details.Count > 0)
        {
            var targetLower = target.ToLowerInvariant();
            foreach (var (key, value) in mudObj.Details)
            {
                if (key.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                    key.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
                {
                    // Found a detail - show it as if reading
                    context.Output($"You read the {key}:");
                    context.Output(value);
                    return Task.CompletedTask;
                }
            }
        }

        // Check player's inventory
        if (readable is null)
        {
            var inventory = context.State.Containers.GetContents(playerId);
            foreach (var objId in inventory)
            {
                var obj = context.State.Objects?.Get<IReadable>(objId);
                if (obj is null) continue;

                if (MatchesTarget(obj, target))
                {
                    readable = obj;
                    readableId = objId;
                    break;
                }
            }
        }

        if (readable is null)
        {
            context.Output($"You don't see anything called '{target}' that you can read.");
            return Task.CompletedTask;
        }

        // Display the readable content
        context.Output($"You read the {readable.ReadableLabel}:");
        context.Output("");
        context.Output(readable.ReadableText);

        return Task.CompletedTask;
    }

    private static bool MatchesTarget(IReadable readable, string target)
    {
        var targetLower = target.ToLowerInvariant();

        // Check name
        if (readable.Name.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check readable label
        if (readable.ReadableLabel.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if it's also an IItem with aliases
        if (readable is IItem item)
        {
            foreach (var alias in item.Aliases)
            {
                if (alias.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                    alias.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
