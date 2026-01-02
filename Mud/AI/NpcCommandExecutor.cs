using System.Text.RegularExpressions;

namespace JitRealm.Mud.AI;

/// <summary>
/// Executes commands on behalf of NPCs. NPCs can issue player-like commands
/// such as "say", "emote", "go", "get", "drop", "kill", "flee".
/// </summary>
public sealed class NpcCommandExecutor
{
    private readonly WorldState _state;
    private readonly IClock _clock;

    // Regex to match *emote* or [emote] patterns (LLMs use both)
    private static readonly Regex EmotePattern = new(@"(\*([^*]+)\*|\[([^\]]+)\])", RegexOptions.Compiled);

    // Regex to detect and fix first-person emotes (I smile → smiles)
    private static readonly Regex FirstPersonPattern = new(@"^I\s+(\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public NpcCommandExecutor(WorldState state, IClock clock)
    {
        _state = state;
        _clock = clock;
    }

    /// <summary>
    /// Parse an LLM response and execute actions.
    /// Emotes are wrapped in *asterisks*, everything else is speech.
    /// Speech before an emote is combined with the emote (up to 2 actions).
    /// Long speech responses are allowed up to a reasonable limit.
    /// </summary>
    /// <param name="npcId">The NPC executing the actions.</param>
    /// <param name="response">The LLM response to parse and execute.</param>
    /// <param name="canSpeak">Whether the NPC can speak.</param>
    /// <param name="canEmote">Whether the NPC can emote.</param>
    public async Task ExecuteLlmResponseAsync(string npcId, string response, bool canSpeak, bool canEmote)
    {
        if (string.IsNullOrWhiteSpace(response))
            return;

        // Find the first emote match
        var match = EmotePattern.Match(response);

        if (match.Success)
        {
            // Check if there's speech BEFORE the first emote
            if (match.Index > 0 && canSpeak)
            {
                var speech = response.Substring(0, match.Index).Trim();
                if (!string.IsNullOrWhiteSpace(speech))
                {
                    // Execute the speech
                    await ExecuteAsync(npcId, $"say {TruncateSpeech(speech)}");
                }
            }

            // Also execute the emote (allows speech + emote combo)
            if (canEmote)
            {
                // Groups[2] = text inside *asterisks*, Groups[3] = text inside [brackets]
                var emoteText = match.Groups[2].Success
                    ? match.Groups[2].Value.Trim()
                    : match.Groups[3].Value.Trim();
                if (!string.IsNullOrWhiteSpace(emoteText))
                {
                    await ExecuteAsync(npcId, $"emote {emoteText}");
                }
            }
            return;
        }

        // No emotes found - treat as speech (allow multiple sentences up to limit)
        if (canSpeak)
        {
            await ExecuteAsync(npcId, $"say {TruncateSpeech(response.Trim())}");
        }
    }

    /// <summary>
    /// Truncate speech to a reasonable length (up to 3 sentences or 300 chars).
    /// Handles ellipsis (...) as a single punctuation, not 3 sentences.
    /// </summary>
    private static string TruncateSpeech(string text)
    {
        const int MaxChars = 300;
        const int MaxSentences = 3;

        if (text.Length <= MaxChars)
        {
            // Count sentences, but handle ellipsis (...) as a single mark
            var sentenceCount = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] is '.' or '!' or '?')
                {
                    // Skip additional dots in ellipsis (... or ..)
                    while (i + 1 < text.Length && text[i + 1] == '.')
                        i++;

                    // Only count as sentence end if followed by space+capital or end of string
                    var isEndOfText = i >= text.Length - 1;
                    var followedByNewSentence = i + 2 < text.Length &&
                        char.IsWhiteSpace(text[i + 1]) &&
                        char.IsUpper(text[i + 2]);

                    if (isEndOfText || followedByNewSentence)
                    {
                        sentenceCount++;
                        if (sentenceCount >= MaxSentences)
                        {
                            return text.Substring(0, i + 1).Trim();
                        }
                    }
                }
            }
            return text;
        }

        // Truncate at last sentence end before MaxChars, or at MaxChars
        var truncated = text.Substring(0, MaxChars);
        var lastSentenceEnd = truncated.LastIndexOfAny(new[] { '.', '!', '?' });
        if (lastSentenceEnd > MaxChars / 2)
        {
            return truncated.Substring(0, lastSentenceEnd + 1).Trim();
        }

        return truncated.Trim() + "...";
    }

    /// <summary>
    /// Execute a command on behalf of an NPC.
    /// </summary>
    /// <param name="npcId">The ID of the NPC executing the command.</param>
    /// <param name="command">The command string (e.g., "emote looks around", "say Hello!").</param>
    /// <returns>True if the command was executed, false if unrecognized or failed.</returns>
    public async Task<bool> ExecuteAsync(string npcId, string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        var cmd = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();

        // Get NPC info
        var npc = _state.Objects?.Get<IMudObject>(npcId);
        if (npc is null)
            return false;

        var npcName = npc.Name;
        var roomId = _state.Containers.GetContainer(npcId);
        if (roomId is null)
            return false;

        // Check capabilities if NPC implements ILlmNpc
        var capabilities = (npc is ILlmNpc llmNpc)
            ? llmNpc.Capabilities
            : NpcCapabilities.Humanoid;

        return cmd switch
        {
            "say" => ExecuteSay(npcId, npcName, roomId, args, capabilities),
            "emote" or "me" => ExecuteEmote(npcId, npcName, roomId, args, capabilities),
            "go" => await ExecuteGoAsync(npcId, npcName, roomId, args, capabilities),
            "n" or "north" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "north" }, capabilities),
            "s" or "south" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "south" }, capabilities),
            "e" or "east" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "east" }, capabilities),
            "w" or "west" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "west" }, capabilities),
            "u" or "up" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "up" }, capabilities),
            "d" or "down" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "down" }, capabilities),
            "get" or "take" => ExecuteGet(npcId, npcName, roomId, args, capabilities),
            "drop" => ExecuteDrop(npcId, npcName, roomId, args, capabilities),
            "kill" or "attack" => ExecuteAttack(npcId, npcName, roomId, args, capabilities),
            "flee" or "retreat" => ExecuteFlee(npcId, npcName, roomId, capabilities),
            _ => false
        };
    }

    private bool ExecuteSay(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanSpeak))
            return false;

        if (args.Length == 0)
            return false;

        var message = string.Join(" ", args);
        _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Say, message, roomId));
        _state.EventLog.Record(roomId, $"{npcName} said: \"{message}\"");

        // Trigger room event for other NPCs
        _ = TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.Speech,
            ActorId = npcId,
            ActorName = npcName,
            Message = message
        }, roomId);

        return true;
    }

    private bool ExecuteEmote(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanEmote))
            return false;

        if (args.Length == 0)
            return false;

        var action = FixFirstPersonEmote(string.Join(" ", args));
        _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Emote, action, roomId));
        _state.EventLog.Record(roomId, $"{npcName} {action}");

        // Trigger room event for other NPCs
        _ = TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.Emote,
            ActorId = npcId,
            ActorName = npcName,
            Message = action
        }, roomId);

        return true;
    }

    private Task<bool> ExecuteGoAsync(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanWander))
            return Task.FromResult(false);

        if (args.Length == 0)
            return Task.FromResult(false);

        var direction = args[0].ToLowerInvariant();

        // Get current room
        var room = _state.Objects?.Get<IRoom>(roomId);
        if (room is null)
            return Task.FromResult(false);

        // Find matching exit
        var exitKey = room.Exits.Keys.FirstOrDefault(k =>
            k.Equals(direction, StringComparison.OrdinalIgnoreCase));

        if (exitKey is null)
            return Task.FromResult(false);

        var destinationId = room.Exits[exitKey];

        // Check if destination room exists
        var destRoom = _state.Objects?.Get<IRoom>(destinationId);
        if (destRoom is null)
            return Task.FromResult(false);

        // Trigger departure event in current room
        _ = TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.Departure,
            ActorId = npcId,
            ActorName = npcName,
            Direction = direction
        }, roomId);

        // Move the NPC
        _state.Containers.Move(npcId, destinationId);

        // Trigger arrival event in new room
        var oppositeDir = GetOppositeDirection(direction);
        _ = TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.Arrival,
            ActorId = npcId,
            ActorName = npcName,
            Direction = oppositeDir
        }, destinationId);

        return Task.FromResult(true);
    }

    private bool ExecuteGet(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanManipulateItems))
            return false;

        if (args.Length == 0)
            return false;

        var itemName = string.Join(" ", args);

        // Find item in room
        var itemId = FindItemInContainer(itemName, roomId);
        if (itemId is null)
            return false;

        var item = _state.Objects?.Get<IItem>(itemId);
        if (item is null)
            return false;

        // Move item to NPC inventory
        _state.Containers.Move(itemId, npcId);

        // Record event
        var itemDisplayName = item.ShortDescription;
        _state.EventLog.Record(roomId, $"{npcName} picks up {itemDisplayName}.");

        // Trigger room event
        _ = TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.ItemTaken,
            ActorId = npcId,
            ActorName = npcName,
            Target = itemDisplayName
        }, roomId);

        return true;
    }

    private bool ExecuteDrop(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanManipulateItems))
            return false;

        if (args.Length == 0)
            return false;

        var itemName = string.Join(" ", args);

        // Find item in NPC inventory
        var itemId = FindItemInContainer(itemName, npcId);
        if (itemId is null)
            return false;

        var item = _state.Objects?.Get<IItem>(itemId);
        if (item is null)
            return false;

        // Move item to room
        _state.Containers.Move(itemId, roomId);

        // Record event
        var itemDisplayName = item.ShortDescription;
        _state.EventLog.Record(roomId, $"{npcName} drops {itemDisplayName}.");

        // Trigger room event
        _ = TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.ItemDropped,
            ActorId = npcId,
            ActorName = npcName,
            Target = itemDisplayName
        }, roomId);

        return true;
    }

    private bool ExecuteAttack(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanAttack))
            return false;

        if (args.Length == 0)
            return false;

        var targetName = string.Join(" ", args).ToLowerInvariant();

        // Find target in room
        var contents = _state.Containers.GetContents(roomId);
        string? targetId = null;
        string? targetDisplayName = null;

        foreach (var objId in contents)
        {
            if (objId == npcId) continue;

            var obj = _state.Objects?.Get<IMudObject>(objId);
            if (obj is null) continue;

            if (obj is ILiving living && obj.Name.ToLowerInvariant().Contains(targetName))
            {
                targetId = objId;
                targetDisplayName = obj.Name;
                break;
            }
        }

        if (targetId is null)
            return false;

        // Start combat
        _state.Combat.StartCombat(npcId, targetId, _clock.Now);

        // Record event
        _state.EventLog.Record(roomId, $"{npcName} attacks {targetDisplayName}!");

        // Trigger room event
        _ = TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.Combat,
            ActorId = npcId,
            ActorName = npcName,
            Target = targetDisplayName
        }, roomId);

        return true;
    }

    private bool ExecuteFlee(string npcId, string npcName, string roomId, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanFlee))
            return false;

        // Check if in combat
        if (!_state.Combat.IsInCombat(npcId))
            return false;

        // Get room exits
        var room = _state.Objects?.Get<IRoom>(roomId);
        if (room is null || room.Exits.Count == 0)
            return false;

        // Random flee chance (50%)
        if (Random.Shared.Next(100) >= 50)
        {
            // Flee failed
            _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Emote, "tries to flee but fails!", roomId));
            return true;
        }

        // Pick random exit
        var exits = room.Exits.ToList();
        var exit = exits[Random.Shared.Next(exits.Count)];

        // End combat
        _state.Combat.EndCombat(npcId);

        // Announce flee
        _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Emote, $"flees {exit.Key}!", roomId));

        // Move NPC
        _state.Containers.Move(npcId, exit.Value);

        return true;
    }

    private string? FindItemInContainer(string name, string containerId)
    {
        var normalizedName = name.ToLowerInvariant();
        var contents = _state.Containers.GetContents(containerId);

        foreach (var itemId in contents)
        {
            var obj = _state.Objects?.Get<IMudObject>(itemId);
            if (obj is null) continue;

            if (obj.Name.ToLowerInvariant().Contains(normalizedName))
                return itemId;

            if (obj is IItem item)
            {
                foreach (var alias in item.Aliases)
                {
                    if (alias.ToLowerInvariant().Contains(normalizedName) ||
                        normalizedName.Contains(alias.ToLowerInvariant()))
                        return itemId;
                }

                if (item.ShortDescription.ToLowerInvariant().Contains(normalizedName))
                    return itemId;
            }
        }

        return null;
    }

    private async Task TriggerRoomEventAsync(RoomEvent roomEvent, string roomId)
    {
        if (_state.Objects is null) return;

        var contents = _state.Containers.GetContents(roomId);
        foreach (var objId in contents)
        {
            if (objId == roomEvent.ActorId) continue;

            var obj = _state.Objects.Get<IMudObject>(objId);
            if (obj is ILlmNpc llmNpc)
            {
                var ctx = _state.CreateContext(objId, _clock);
                await llmNpc.OnRoomEventAsync(roomEvent, ctx);
            }
        }
    }

    private static string? GetOppositeDirection(string direction)
    {
        return direction.ToLowerInvariant() switch
        {
            "north" or "n" => "south",
            "south" or "s" => "north",
            "east" or "e" => "west",
            "west" or "w" => "east",
            "up" or "u" => "below",
            "down" or "d" => "above",
            "northeast" or "ne" => "southwest",
            "northwest" or "nw" => "southeast",
            "southeast" or "se" => "northwest",
            "southwest" or "sw" => "northeast",
            _ => null
        };
    }

    /// <summary>
    /// Convert first-person emotes to third-person.
    /// "I smile" → "smiles", "I look around" → "looks around"
    /// </summary>
    private static string FixFirstPersonEmote(string emote)
    {
        var match = FirstPersonPattern.Match(emote);
        if (!match.Success)
            return emote;

        var verb = match.Groups[1].Value;
        var rest = emote.Substring(match.Length).TrimStart();

        // Convert verb to third-person (simple -s/-es rule)
        var thirdPerson = verb.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
                          verb.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
                          verb.EndsWith("z", StringComparison.OrdinalIgnoreCase) ||
                          verb.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
                          verb.EndsWith("sh", StringComparison.OrdinalIgnoreCase)
            ? verb + "es"
            : verb + "s";

        return string.IsNullOrEmpty(rest) ? thirdPerson : $"{thirdPerson} {rest}";
    }
}
