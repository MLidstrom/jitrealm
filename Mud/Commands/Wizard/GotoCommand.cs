using JitRealm.Mud.Security;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to teleport to locations.
/// Supports "goto home" to teleport to the wizard's home room,
/// "goto {npc-name}" to teleport to an NPC's location,
/// or "goto {room-id}" to teleport to any room.
/// </summary>
public class GotoCommand : WizardCommandBase
{
    public override string Name => "goto";
    public override IReadOnlyList<string> Aliases => new[] { "go", "teleport", "tp" };
    public override string Usage => "goto <home|npc-name|room-id>";
    public override string Description => "Teleport to a location (home, NPC, or room ID)";

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
            roomBlueprintId = $"Rooms/Homes/{firstLetter}/{normalizedName}/home.cs";
        }
        else
        {
            // First, try to find a living (NPC/player) by name or alias
            var search = string.Join(" ", args).ToLowerInvariant();
            var foundLiving = FindLivingByNameOrAlias(context, search);

            if (foundLiving is not null)
            {
                // Get the living's container (room)
                var livingRoomId = context.State.Containers.GetContainer(foundLiving.Value.instanceId);
                if (livingRoomId is null)
                {
                    context.Output($"{foundLiving.Value.name} is not in any room.");
                    return;
                }

                // Get the room object
                var livingRoom = context.State.Objects!.Get<IRoom>(livingRoomId);
                if (livingRoom is null)
                {
                    context.Output($"Cannot find room for {foundLiving.Value.name}.");
                    return;
                }

                // Teleport to that room
                await TeleportToRoom(context, livingRoom);
                context.Output($"You teleport to {foundLiving.Value.name}'s location.");
                ShowRoom(context, livingRoom);
                return;
            }

            // Use the provided room ID directly
            roomBlueprintId = string.Join(" ", args);

            // Strip .cs extension if provided
            if (roomBlueprintId.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                roomBlueprintId = roomBlueprintId[..^3];
            }
        }

        // Try to load the destination room
        IRoom? destRoom = null;
        try
        {
            destRoom = await context.State.Objects!.LoadAsync<IRoom>(roomBlueprintId, context.State);
        }
        catch
        {
            // Room load failed - will handle below
        }

        if (destRoom is null)
        {
            // Check if this might be an NPC name that isn't spawned yet
            var search = string.Join(" ", args).ToLowerInvariant();
            if (!search.Contains("/") && !search.Contains("room"))
            {
                context.Output($"No NPC '{search}' found (may not be spawned yet) and no room '{roomBlueprintId}' exists.");
                context.Output("Hint: Visit the room where the NPC spawns first, or use 'where' to find loaded NPCs.");
            }
            else
            {
                context.Output($"Room not found: {roomBlueprintId}");
            }
            return;
        }

        await TeleportToRoom(context, destRoom);
        context.Output($"You teleport to {destRoom.Name}.");
        ShowRoom(context, destRoom);
    }

    /// <summary>
    /// Find a living (NPC/player) by name or alias.
    /// Prioritizes exact matches over partial matches.
    /// </summary>
    private static (string instanceId, string name)? FindLivingByNameOrAlias(CommandContext context, string search)
    {
        // First pass: exact matches
        foreach (var instanceId in context.State.Objects!.ListInstanceIds())
        {
            var obj = context.State.Objects.Get<IMudObject>(instanceId);
            if (obj is not ILiving living) continue;

            // Exact name match
            if (obj.Name?.Equals(search, StringComparison.OrdinalIgnoreCase) == true)
            {
                return (instanceId, obj.Name);
            }

            // Exact alias match
            if (living.Aliases.Any(a => a.Equals(search, StringComparison.OrdinalIgnoreCase)))
            {
                return (instanceId, obj.Name ?? instanceId);
            }
        }

        // Second pass: partial matches
        foreach (var instanceId in context.State.Objects!.ListInstanceIds())
        {
            var obj = context.State.Objects.Get<IMudObject>(instanceId);
            if (obj is not ILiving living) continue;

            // Match by name containing search
            if (obj.Name?.ToLowerInvariant().Contains(search) == true)
            {
                return (instanceId, obj.Name);
            }

            // Match by alias containing search
            if (living.Aliases.Any(a => a.ToLowerInvariant().Contains(search)))
            {
                return (instanceId, obj.Name ?? instanceId);
            }
        }

        return null;
    }

    /// <summary>
    /// Perform the actual teleportation to a room.
    /// </summary>
    private static async Task TeleportToRoom(CommandContext context, IRoom destRoom)
    {
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

        // Process spawns for any linked rooms
        if (destRoom is IHasLinkedRooms hasLinkedRooms)
        {
            foreach (var linkedRoomId in hasLinkedRooms.LinkedRooms)
            {
                var linkedRoom = await context.State.Objects!.LoadAsync<IRoom>(linkedRoomId, context.State);
                await context.State.ProcessSpawnsAsync(linkedRoom.Id, new SystemClock());
            }
        }

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

        // Show exits (filter hidden ones)
        var visibleExits = room.Exits.Keys.Where(e => !room.HiddenExits.Contains(e)).ToList();
        if (visibleExits.Count > 0)
        {
            lines.Add($"Exits: {string.Join(", ", visibleExits)}");
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
