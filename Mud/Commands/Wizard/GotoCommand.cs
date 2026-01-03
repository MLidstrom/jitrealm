using JitRealm.Mud.Security;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to teleport to locations.
/// Supports "goto home" to teleport to the wizard's home room,
/// or "goto {room-id}" to teleport to any room.
/// </summary>
public class GotoCommand : WizardCommandBase
{
    public override string Name => "goto";
    public override IReadOnlyList<string> Aliases => new[] { "teleport", "tp" };
    public override string Usage => "goto <home|room-id>";
    public override string Description => "Teleport to a location (home or room ID)";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return;

        var destination = args[0].ToLowerInvariant();
        string roomBlueprintId;

        if (destination == "home")
        {
            // Resolve home room path: Rooms/Homes/{first_letter}/{name}/home.cs
            var playerName = context.Session.PlayerName;
            if (string.IsNullOrEmpty(playerName))
            {
                context.Output("Cannot determine player name.");
                return;
            }

            var normalizedName = playerName.ToLowerInvariant();
            var firstLetter = normalizedName[0];
            roomBlueprintId = $"Rooms/Homes/{firstLetter}/{normalizedName}/home";
        }
        else
        {
            // Use the provided room ID directly
            roomBlueprintId = string.Join(" ", args);

            // Strip .cs extension if provided
            if (roomBlueprintId.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                roomBlueprintId = roomBlueprintId[..^3];
            }
        }

        // Try to load the destination room
        IRoom destRoom;
        try
        {
            destRoom = await context.State.Objects!.LoadAsync<IRoom>(roomBlueprintId, context.State);
        }
        catch (Exception ex)
        {
            context.Output($"Cannot load room '{roomBlueprintId}': {ex.Message}");
            return;
        }

        if (destRoom is null)
        {
            context.Output($"Room not found: {roomBlueprintId}");
            return;
        }

        var playerId = context.PlayerId;
        var currentRoomId = context.GetPlayerLocation();

        // Call IOnLeave hook on current room if applicable
        if (currentRoomId is not null)
        {
            var currentRoom = context.State.Objects!.Get<IRoom>(currentRoomId);
            if (currentRoom is IOnLeave onLeave)
            {
                var ctx = context.CreateContext(currentRoomId);
                SafeInvoker.TryInvokeHook(() => onLeave.OnLeave(ctx, playerId), $"OnLeave in {currentRoomId}");
            }

            // Notify others in current room
            BroadcastToRoom(context, currentRoomId, $"{context.Session.PlayerName} vanishes in a puff of smoke.");
        }

        // Process spawns for the destination room
        await context.State.ProcessSpawnsAsync(destRoom.Id, new SystemClock());

        // Move player to destination
        context.State.Containers.Move(playerId, destRoom.Id);

        // Call IOnEnter hook on destination room
        if (destRoom is IOnEnter onEnter)
        {
            var ctx = context.CreateContext(destRoom.Id);
            SafeInvoker.TryInvokeHook(() => onEnter.OnEnter(ctx, playerId), $"OnEnter in {destRoom.Id}");
        }

        // Notify others in destination room
        BroadcastToRoom(context, destRoom.Id, $"{context.Session.PlayerName} appears in a puff of smoke.");

        // Show the new room
        context.Output($"You teleport to {destRoom.Name}.");
        ShowRoom(context, destRoom);
    }

    private static void BroadcastToRoom(CommandContext context, string roomId, string message)
    {
        var sessions = context.State.Sessions.GetAll();
        foreach (var session in sessions)
        {
            if (session.PlayerId is null || session.PlayerId == context.PlayerId)
                continue;

            var playerRoom = context.State.Containers.GetContainer(session.PlayerId);
            if (playerRoom == roomId)
            {
                _ = session.WriteLineAsync(message);
            }
        }
    }

    private static void ShowRoom(CommandContext context, IRoom room)
    {
        var lines = new List<string>
        {
            $"=== {room.Name} ===",
            room.Description
        };

        // Show exits
        if (room.Exits.Count > 0)
        {
            lines.Add($"Exits: {string.Join(", ", room.Exits.Keys)}");
        }

        // Show room contents (items and NPCs)
        var contents = context.State.Containers.GetContents(room.Id);
        var items = new List<string>();
        var beings = new List<string>();

        foreach (var objId in contents)
        {
            if (objId == context.PlayerId)
                continue;

            var obj = context.State.Objects!.Get<IMudObject>(objId);
            if (obj is null)
                continue;

            if (obj is IPlayer)
            {
                var session = context.State.Sessions.GetByPlayerId(objId);
                beings.Add(session?.PlayerName ?? obj.Name);
            }
            else if (obj is ILiving)
            {
                beings.Add(obj.Name);
            }
            else if (obj is IItem item)
            {
                items.Add(item.ShortDescription);
            }
        }

        if (beings.Count > 0)
        {
            lines.Add($"You see: {string.Join(", ", beings)}");
        }

        if (items.Count > 0)
        {
            lines.Add($"Items: {string.Join(", ", items)}");
        }

        context.Output(string.Join("\n", lines));
    }
}
