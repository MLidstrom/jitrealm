namespace JitRealm.Mud.Commands.Navigation;

/// <summary>
/// Look at the current room or examine a specific target.
/// </summary>
public class LookCommand : CommandBase
{
    public override string Name => "look";
    public override IReadOnlyList<string> Aliases => new[] { "l" };
    public override string Usage => "look [at <target>]";
    public override string Description => "Look at your surroundings or examine something";
    public override string Category => "Navigation";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            await LookAtRoomAsync(context);
        }
        else
        {
            // "look at X" or "look X"
            var target = args[0].Equals("at", StringComparison.OrdinalIgnoreCase) && args.Length > 1
                ? string.Join(" ", args.Skip(1))
                : string.Join(" ", args);
            await LookAtTargetAsync(context, target);
        }
    }

    private async Task LookAtRoomAsync(CommandContext context)
    {
        var room = context.GetCurrentRoom();
        if (room is null)
        {
            // Try to load the room
            var roomId = context.GetPlayerLocation();
            if (roomId is null)
            {
                context.Output("You are nowhere.");
                return;
            }
            room = await context.State.Objects!.LoadAsync<IRoom>(roomId, context.State);
        }

        context.Output("");
        context.Output($"=== {room.Name} ===");
        context.Output(room.Description);

        // Show time and weather for outdoor rooms
        if (room.IsOutdoors)
        {
            var timeWeatherLine = GetTimeWeatherDescription(context);
            if (!string.IsNullOrEmpty(timeWeatherLine))
            {
                context.Output(timeWeatherLine);
            }
        }

        // Filter out hidden exits
        var visibleExits = room.Exits.Keys.Where(e => !room.HiddenExits.Contains(e)).ToList();
        if (visibleExits.Count > 0)
            context.Output("Exits: " + string.Join(", ", visibleExits));

        // Get contents from container registry
        var contents = context.State.Containers.GetContents(room.Id);
        if (contents.Count > 0)
        {
            var names = new List<string>();
            foreach (var objId in contents)
            {
                // Don't show the player themselves
                if (objId == context.PlayerId) continue;

                var obj = context.State.Objects!.Get<IMudObject>(objId);
                if (obj is null)
                {
                    names.Add(objId);
                }
                else if (obj is ILiving living)
                {
                    if (obj is IPlayer)
                    {
                        // Show player name (not "a <name>")
                        var session = context.State.Sessions.GetByPlayerId(objId);
                        names.Add(session?.PlayerName ?? obj.Name);
                    }
                    else
                    {
                        // Use ShortDescription for non-player living entities (includes article)
                        names.Add(living.ShortDescription);
                    }
                }
                else if (obj is IItem item)
                {
                    // Use ShortDescription for items (includes article)
                    names.Add(item.ShortDescription);
                }
                else
                {
                    names.Add(obj.Name);
                }
            }
            if (names.Count > 0)
                context.Output("You see: " + string.Join(", ", names));
        }
    }

    private Task LookAtTargetAsync(CommandContext context, string target)
    {
        var room = context.GetCurrentRoom();
        if (room is null)
        {
            context.Output("You are nowhere.");
            return Task.CompletedTask;
        }

        var normalizedTarget = target.ToLowerInvariant();

        // Check for self-reference
        if (normalizedTarget is "me" or "self" or "myself")
        {
            LookAtSelf(context);
            return Task.CompletedTask;
        }

        // Check room details
        foreach (var (keyword, description) in room.Details)
        {
            if (keyword.ToLowerInvariant().Contains(normalizedTarget) ||
                normalizedTarget.Contains(keyword.ToLowerInvariant()))
            {
                context.Output(description);
                return Task.CompletedTask;
            }
        }

        // Check items in inventory
        var ctx = context.CreateContext(context.PlayerId);
        var itemId = ctx.FindItem(target, context.PlayerId);
        if (itemId is not null)
        {
            var item = context.State.Objects!.Get<IItem>(itemId);
            if (item is not null)
            {
                // Check item details first
                foreach (var (keyword, description) in item.Details)
                {
                    if (keyword.ToLowerInvariant().Contains(normalizedTarget) ||
                        normalizedTarget.Contains(keyword.ToLowerInvariant()))
                    {
                        context.Output(description);
                        return Task.CompletedTask;
                    }
                }
                // Fall back to item description
                context.Output(item.Description);
                return Task.CompletedTask;
            }
        }

        // Check all objects in room by name and aliases
        var contents = context.State.Containers.GetContents(room.Id);
        foreach (var objId in contents)
        {
            if (objId == context.PlayerId) continue;

            var obj = context.State.Objects!.Get<IMudObject>(objId);
            if (obj is null) continue;

            // Check if name matches
            bool matches = obj.Name.ToLowerInvariant().Contains(normalizedTarget);

            // For IItem, also check aliases and ShortDescription
            if (!matches && obj is IItem itemObj)
            {
                foreach (var alias in itemObj.Aliases)
                {
                    if (alias.ToLowerInvariant().Contains(normalizedTarget) ||
                        normalizedTarget.Contains(alias.ToLowerInvariant()))
                    {
                        matches = true;
                        break;
                    }
                }
                if (!matches && itemObj.ShortDescription.ToLowerInvariant().Contains(normalizedTarget))
                {
                    matches = true;
                }
            }

            // For ILiving, also check aliases (e.g., "barnaby" for shopkeeper)
            if (!matches && obj is ILiving livingObj)
            {
                foreach (var alias in livingObj.Aliases)
                {
                    if (alias.ToLowerInvariant().Contains(normalizedTarget) ||
                        normalizedTarget.Contains(alias.ToLowerInvariant()))
                    {
                        matches = true;
                        break;
                    }
                }
            }

            if (matches)
            {
                // Check object details first
                foreach (var (keyword, description) in obj.Details)
                {
                    if (keyword.ToLowerInvariant().Contains(normalizedTarget) ||
                        normalizedTarget.Contains(keyword.ToLowerInvariant()))
                    {
                        context.Output(description);
                        return Task.CompletedTask;
                    }
                }
                // For items, show description
                if (obj is IItem item)
                {
                    context.Output(item.Description);
                    return Task.CompletedTask;
                }
                // For livings, show their description, HP, and inventory
                if (obj is ILiving living)
                {
                    context.Output(living.Description);
                    context.Output($"  HP: {living.HP}/{living.MaxHP}");

                    // Show what they're carrying
                    var inventory = context.State.Containers.GetContents(objId);
                    var carriedItems = new List<string>();
                    foreach (var carriedItemId in inventory)
                    {
                        var carriedItem = context.State.Objects?.Get<IItem>(carriedItemId);
                        if (carriedItem is not null)
                        {
                            carriedItems.Add(carriedItem.ShortDescription);
                        }
                    }
                    if (carriedItems.Count > 0)
                    {
                        var formatted = ItemFormatter.FormatGroupedList(carriedItems);
                        context.Output($"  Carrying: {formatted}");
                    }

                    return Task.CompletedTask;
                }
                context.Output($"You see {obj.Name}.");
                return Task.CompletedTask;
            }
        }

        context.Output($"You don't see '{target}' here.");
        return Task.CompletedTask;
    }

    private void LookAtSelf(CommandContext context)
    {
        var player = context.GetPlayer();
        if (player is null)
        {
            context.Output("You examine yourself but feel strangely disconnected.");
            return;
        }

        var playerName = context.Session.PlayerName ?? "Unknown";
        if (context.IsWizard)
        {
            context.Output($"{playerName} the Wizard.");
            context.Output("  You radiate an aura of power and knowledge.");
        }
        else
        {
            context.Output($"{playerName} the level {player.Level} adventurer.");
            context.Output($"  HP: {player.HP}/{player.MaxHP}");
            context.Output($"  Experience: {player.Experience}");
        }

        // Show equipment summary
        var equipped = context.State.Equipment.GetAllEquipped(context.PlayerId);
        if (equipped.Count > 0)
        {
            context.Output("  Equipped:");
            foreach (var (slot, eqItemId) in equipped)
            {
                var eqItem = context.State.Objects!.Get<IItem>(eqItemId);
                context.Output($"    {slot}: {eqItem?.ShortDescription ?? eqItemId}");
            }
        }
    }

    /// <summary>
    /// Gets a combined time and weather description for outdoor rooms.
    /// </summary>
    private static string GetTimeWeatherDescription(CommandContext context)
    {
        var parts = new List<string>();

        // Get time from TIME_D daemon (using interface for runtime type resolution)
        var timeD = context.State.Daemons.Get<ITimeDaemon>("TIME_D");
        if (timeD is not null)
        {
            parts.Add(timeD.PeriodDescription);
        }

        // Get weather from WEATHER_D daemon (using interface for runtime type resolution)
        var weatherD = context.State.Daemons.Get<IWeatherDaemon>("WEATHER_D");
        if (weatherD is not null)
        {
            parts.Add(weatherD.WeatherDescription);
        }

        return string.Join(" ", parts);
    }
}
