namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to remove exits from the current room.
/// </summary>
public class UnlinkCommand : WizardCommandBase
{
    public override string Name => "unlink";
    public override string[] Aliases => new[] { "disconnect", "removeExit" };
    public override string Usage => "unlink <direction> [--both]";
    public override string Description => "Remove an exit from the current room";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage(context);
            return;
        }

        // Parse arguments
        var direction = args[0];
        var removeBoth = args.Any(a => a.Equals("--both", StringComparison.OrdinalIgnoreCase));

        // Validate direction
        var normalizedDir = DirectionHelper.NormalizeDirection(direction);

        // Get current room
        var currentRoomId = context.GetPlayerLocation();
        if (currentRoomId is null)
        {
            context.Output("You are not in a room.");
            return;
        }

        // Get current room object to find the target
        var currentRoom = context.State.Objects!.Get<IRoom>(currentRoomId);
        if (currentRoom is null)
        {
            context.Output("Cannot access current room.");
            return;
        }

        // Check if exit exists
        if (!currentRoom.Exits.TryGetValue(normalizedDir, out var targetBlueprintId))
        {
            context.Output($"No exit '{normalizedDir}' in this room.");
            context.Output($"Available exits: {string.Join(", ", currentRoom.Exits.Keys)}");
            return;
        }

        // Get world root and paths
        var worldRoot = WizardFilesystem.GetWorldRoot(context);
        var currentBlueprintId = GetBlueprintIdFromInstanceId(currentRoomId);
        var currentFilePath = RoomFileEditor.GetFilePathFromBlueprintId(currentBlueprintId, worldRoot);

        if (!File.Exists(currentFilePath))
        {
            context.Output($"Cannot modify current room: file not found at {currentFilePath}");
            context.Output("This may be a generated or non-file-based room.");
            return;
        }

        // Remove exit from current room
        var result = await RoomFileEditor.RemoveExitAsync(currentFilePath, normalizedDir);
        if (!result.Success)
        {
            context.Output($"Error: {result.ErrorMessage}");
            return;
        }

        context.Output($"Removed exit '{normalizedDir}' from current room.");

        // Remove reverse exit from target room (if --both)
        string? targetFilePath = null;
        if (removeBoth)
        {
            var reverseDir = DirectionHelper.GetReverseDirection(normalizedDir);
            if (reverseDir is not null)
            {
                // Normalize target blueprint ID (remove .cs if present)
                var normalizedTarget = targetBlueprintId;
                if (normalizedTarget.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedTarget = normalizedTarget[..^3];
                }

                targetFilePath = RoomFileEditor.GetFilePathFromBlueprintId(normalizedTarget, worldRoot);

                if (File.Exists(targetFilePath))
                {
                    // Check if target has a reverse exit pointing back here
                    if (RoomFileEditor.HasExit(targetFilePath, reverseDir))
                    {
                        var reverseResult = await RoomFileEditor.RemoveExitAsync(targetFilePath, reverseDir);
                        if (reverseResult.Success)
                        {
                            context.Output($"Removed exit '{reverseDir}' from {normalizedTarget}.");
                        }
                        else
                        {
                            context.Output($"Warning: Could not remove reverse exit: {reverseResult.ErrorMessage}");
                        }
                    }
                    else
                    {
                        context.Output($"Note: {normalizedTarget} has no '{reverseDir}' exit to remove.");
                    }
                }
                else
                {
                    context.Output($"Warning: Target room file not found, cannot remove reverse exit.");
                }
            }
            else
            {
                context.Output($"Note: No reverse direction for '{normalizedDir}'.");
            }
        }
        else
        {
            // Show hint about reverse exit
            var reverseDir = DirectionHelper.GetReverseDirection(normalizedDir);
            if (reverseDir is not null)
            {
                context.Output($"Note: {targetBlueprintId} may still have a '{reverseDir}' exit back here.");
                context.Output($"Use 'unlink {direction} --both' to remove both directions.");
            }
        }

        // Reload affected rooms
        context.Output("Reloading affected rooms...");
        try
        {
            await context.State.Objects!.ReloadBlueprintAsync(currentBlueprintId, context.State);

            if (removeBoth && targetFilePath is not null && File.Exists(targetFilePath))
            {
                var normalizedTarget = targetBlueprintId;
                if (normalizedTarget.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedTarget = normalizedTarget[..^3];
                }
                await context.State.Objects!.ReloadBlueprintAsync(normalizedTarget, context.State);
            }
        }
        catch (Exception ex)
        {
            context.Output($"Warning: Reload failed: {ex.Message}");
            context.Output("You may need to manually reload with 'reload <room>'.");
        }

        context.Output("Done.");
    }

    private void ShowUsage(CommandContext context)
    {
        context.Output("Usage: unlink <direction> [--both]");
        context.Output("");
        context.Output("Removes an exit from the current room.");
        context.Output("");
        context.Output("Options:");
        context.Output("  --both   Also remove the reverse exit from the target room");
        context.Output("");
        context.Output("Examples:");
        context.Output("  unlink north         - Remove north exit from current room only");
        context.Output("  unlink north --both  - Remove both north and the reverse south exit");
        context.Output("");
        context.Output("Use 'link' to add exits, 'dig' to create new rooms with exits.");
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
