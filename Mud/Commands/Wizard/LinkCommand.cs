namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to link the current room to an existing room.
/// Creates bidirectional exits by default.
/// </summary>
public class LinkCommand : WizardCommandBase
{
    public override string Name => "link";
    public override string[] Aliases => new[] { "connect", "addExit" };
    public override string Usage => "link <direction> <room-path> [--oneway] [--hidden]";
    public override string Description => "Link current room to an existing room";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage(context);
            return;
        }

        // Parse arguments
        var direction = args[0];
        var targetPath = args[1];
        var isOneWay = args.Any(a => a.Equals("--oneway", StringComparison.OrdinalIgnoreCase));
        var isHidden = args.Any(a => a.Equals("--hidden", StringComparison.OrdinalIgnoreCase));

        // Validate direction
        var normalizedDir = DirectionHelper.NormalizeDirection(direction);
        if (!DirectionHelper.IsValidDirection(normalizedDir))
        {
            context.Output($"Unknown direction: {direction}");
            context.Output($"Valid directions: {DirectionHelper.GetValidDirectionsList()}");
            return;
        }

        // Get current room
        var currentRoomId = context.GetPlayerLocation();
        if (currentRoomId is null)
        {
            context.Output("You are not in a room.");
            return;
        }

        // Get world root and paths
        var worldRoot = WizardFilesystem.GetWorldRoot(context);

        // Normalize target path (strip .cs if present, ensure it doesn't start with /)
        var targetBlueprintId = targetPath.TrimStart('/');
        if (targetBlueprintId.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            targetBlueprintId = targetBlueprintId[..^3];
        }

        var targetFilePath = RoomFileEditor.GetFilePathFromBlueprintId(targetBlueprintId, worldRoot);

        // Validate target room exists
        if (!File.Exists(targetFilePath))
        {
            context.Output($"Room not found: {targetBlueprintId}.cs");
            context.Output($"Use 'dig {direction} {Path.GetFileNameWithoutExtension(targetBlueprintId)}' to create it.");
            return;
        }

        context.Output($"Validating {targetBlueprintId}.cs... OK");

        // Get current room's blueprint ID and file path
        var currentBlueprintId = GetBlueprintIdFromInstanceId(currentRoomId);
        var currentFilePath = RoomFileEditor.GetFilePathFromBlueprintId(currentBlueprintId, worldRoot);

        if (!File.Exists(currentFilePath))
        {
            context.Output($"Cannot modify current room: file not found at {currentFilePath}");
            context.Output("This may be a generated or non-file-based room.");
            return;
        }

        // Check for self-link
        if (currentBlueprintId.Equals(targetBlueprintId, StringComparison.OrdinalIgnoreCase))
        {
            context.Output("Cannot link a room to itself.");
            return;
        }

        // Add exit to current room
        var result = await RoomFileEditor.AddExitAsync(currentFilePath, normalizedDir, targetBlueprintId);
        if (!result.Success)
        {
            context.Output($"Error: {result.ErrorMessage}");
            return;
        }

        context.Output($"Added exit '{normalizedDir}' to current room → {targetBlueprintId}");

        // Add reverse exit to target room (unless --oneway)
        if (!isOneWay)
        {
            var reverseDir = DirectionHelper.GetReverseDirection(normalizedDir);
            if (reverseDir is not null)
            {
                var reverseResult = await RoomFileEditor.AddExitAsync(targetFilePath, reverseDir, currentBlueprintId);
                if (reverseResult.Success)
                {
                    context.Output($"Added exit '{reverseDir}' to {targetBlueprintId} → {currentBlueprintId}");
                }
                else
                {
                    context.Output($"Warning: Could not add reverse exit: {reverseResult.ErrorMessage}");
                }
            }
            else
            {
                context.Output($"Warning: No reverse direction for '{normalizedDir}'. Creating one-way exit.");
            }
        }
        else
        {
            context.Output($"(one-way exit - no reverse link created)");
        }

        // TODO: Support --hidden flag by adding to HiddenExits property

        // Reload affected rooms
        context.Output("Reloading affected rooms...");
        try
        {
            await context.State.Objects!.ReloadBlueprintAsync(currentBlueprintId, context.State);
            if (!isOneWay)
            {
                await context.State.Objects!.ReloadBlueprintAsync(targetBlueprintId, context.State);
            }
        }
        catch (Exception ex)
        {
            context.Output($"Warning: Reload failed: {ex.Message}");
            context.Output("You may need to manually reload with 'reload <room>'.");
        }

        context.Output("Done.");
        context.Output($"Use 'go {normalizedDir}' to visit the linked room.");
    }

    private void ShowUsage(CommandContext context)
    {
        context.Output("Usage: link <direction> <room-path> [--oneway] [--hidden]");
        context.Output("");
        context.Output("Links the current room to an existing room by adding exits.");
        context.Output("By default, creates bidirectional exits (both directions).");
        context.Output("");
        context.Output("Options:");
        context.Output("  --oneway   Only create exit from current room (no reverse)");
        context.Output("  --hidden   Make the exit hidden (not shown in room description)");
        context.Output("");
        context.Output("Examples:");
        context.Output("  link north Rooms/tavern      - Link to tavern (both directions)");
        context.Output("  link east Rooms/shop --oneway - One-way link to shop");
        context.Output("  link down Rooms/cellar       - Link down to cellar");
        context.Output("");
        context.Output("Room paths are relative to World/ directory.");
        context.Output("Use 'dig' to create a new room, 'unlink' to remove exits.");
    }

    /// <summary>
    /// Extract blueprint ID from instance ID (removes #NNNNNN suffix).
    /// </summary>
    private static string GetBlueprintIdFromInstanceId(string instanceId)
    {
        var hashIndex = instanceId.IndexOf('#');
        return hashIndex > 0 ? instanceId[..hashIndex] : instanceId;
    }
}
