using JitRealm.Mud.Security;

namespace JitRealm.Mud.Commands.Navigation;

/// <summary>
/// Move to another room through an exit.
/// </summary>
public class GoCommand : CommandBase
{
    private static readonly string[] DirectionAliases = new[]
    {
        "north", "south", "east", "west", "up", "down",
        "n", "s", "e", "w", "u", "d",
        "northeast", "northwest", "southeast", "southwest",
        "ne", "nw", "se", "sw",
        "in", "out", "enter", "leave"
    };

    public override string Name => "go";
    public override IReadOnlyList<string> Aliases => DirectionAliases;
    public override string Usage => "go <direction>";
    public override string Description => "Move in a direction";
    public override string Category => "Navigation";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        // Get the direction - either from args or from the command name itself
        string direction;
        if (args.Length > 0)
        {
            direction = args[0].ToLowerInvariant();
        }
        else
        {
            // The command was invoked directly as a direction (e.g., "north" instead of "go north")
            var rawInput = context.RawInput.Trim().ToLowerInvariant();
            var parts = rawInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            direction = parts[0];

            // If invoked as "go" without args, show usage
            if (direction == "go")
            {
                context.Output("Usage: go <direction>");
                return;
            }
        }

        // Expand short directions
        direction = ExpandDirection(direction);

        var currentRoom = context.GetCurrentRoom();
        if (currentRoom is null)
        {
            var roomId = context.GetPlayerLocation();
            if (roomId is null)
            {
                context.Output("You are nowhere.");
                return;
            }
            currentRoom = await context.State.Objects!.LoadAsync<IRoom>(roomId, context.State);
        }

        if (!currentRoom.Exits.TryGetValue(direction, out var destId))
        {
            context.Output("You can't go that way.");
            return;
        }

        // Call IOnLeave hook on current room
        if (currentRoom is IOnLeave onLeave)
        {
            var ctx = context.CreateContext(currentRoom.Id);
            SafeInvoker.TryInvokeHook(() => onLeave.OnLeave(ctx, context.PlayerId), $"OnLeave in {currentRoom.Id}");
        }

        var dest = await context.State.Objects!.LoadAsync<IRoom>(destId, context.State);

        // Process spawns for the destination room
        await context.State.ProcessSpawnsAsync(dest.Id, context.State.Clock);

        // Move player to new room
        context.State.Containers.Move(context.PlayerId, dest.Id);

        // Call IOnEnter hook on destination room
        if (dest is IOnEnter onEnter)
        {
            var ctx = context.CreateContext(dest.Id);
            SafeInvoker.TryInvokeHook(() => onEnter.OnEnter(ctx, context.PlayerId), $"OnEnter in {dest.Id}");
        }

        // Look at the new room
        await new LookCommand().ExecuteAsync(context, Array.Empty<string>());
    }

    private static string ExpandDirection(string dir)
    {
        return dir switch
        {
            "n" => "north",
            "s" => "south",
            "e" => "east",
            "w" => "west",
            "u" => "up",
            "d" => "down",
            "ne" => "northeast",
            "nw" => "northwest",
            "se" => "southeast",
            "sw" => "southwest",
            _ => dir
        };
    }
}
