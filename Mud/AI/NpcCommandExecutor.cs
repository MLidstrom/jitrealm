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

    // Track the current interactor during command execution (who the NPC is responding to)
    private string? _currentInteractorId;

    // Track the last failure reason for command feedback
    private string? _lastFailureReason;

    // State key for storing last command results
    private const string LastCommandResultsKey = "_npc_last_cmd_results";

    // Regex to match *emote* or [emote] patterns (LLMs use both)
    private static readonly Regex EmotePattern = new(@"(\*([^*]+)\*|\[([^\]]+)\])", RegexOptions.Compiled);

    // Regex to detect and fix first-person emotes (I smile → smiles)
    private static readonly Regex FirstPersonPattern = new(@"^I\s+(\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to match command markup: [cmd:command args] or {cmd:command args}
    private static readonly Regex CommandMarkupPattern = new(@"[\[{]cmd:([^\]}]+)[\]}]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to match goal markup: [goal:type] or goal:type (brackets optional)
    // Also supports [goal:type target]
    private static readonly Regex GoalMarkupPattern = new(@"(?:[\[{])?goal:([^\]}\r\n]+)(?:[\]}])?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to match goal clear: [goal:clear], [goal:done], goal:clear, etc. (brackets optional)
    // Captures the full content after goal: for patterns starting with clear/done/complete/none
    private static readonly Regex GoalClearPattern = new(@"(?:[\[{])?goal:((?:clear|done|complete|none)(?:\s+[^\]}\r\n]*)?)(?:[\]}])?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to match plan markup: [plan:step1|step2|step3] or plan:step1|step2|step3 (brackets optional)
    // Also supports [plan:goalType:step1|step2|step3]
    // Captures full content after "plan:" for further parsing
    private static readonly Regex PlanMarkupPattern = new(@"(?:[\[{])?plan:([^\]}\r\n]+)(?:[\]}])?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex to match step actions: [step:done], [step:skip], step:done, step:skip (brackets optional)
    // Also supports [step:goalType:done], [step:goalType:skip]
    // Captures full content after "step:" for further parsing
    private static readonly Regex StepActionPattern = new(@"(?:[\[{])?step:([^\]}\r\n]+)(?:[\]}])?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Commands that NPCs are forbidden from using via [cmd:...] markup
    private static readonly HashSet<string> ForbiddenCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "quit", "logout", "exit", "password", "save", "delete", "suicide",
        "patch", "stat", "destruct", "reset", "goto", "pwd", "ls", "cd", "cat", "more", "edit", "ledit", "perf",
        // Say/emote must use natural format (*emote* or "speech"), not [cmd:...] markup
        "say", "emote", "me", "'"
    };

    public NpcCommandExecutor(WorldState state, IClock clock)
    {
        _state = state;
        _clock = clock;
    }

    /// <summary>
    /// Parse an LLM response and execute actions in sequential order.
    /// Emotes (*asterisks*), commands ([cmd:...]), and speech ("quotes") are all
    /// executed in the order they appear in the response.
    /// </summary>
    /// <param name="npcId">The NPC executing the actions.</param>
    /// <param name="response">The LLM response to parse and execute.</param>
    /// <param name="canSpeak">Whether the NPC can speak.</param>
    /// <param name="canEmote">Whether the NPC can emote.</param>
    /// <param name="interactorId">Optional ID of who the NPC is responding to (for "player" resolution).</param>
    public async Task ExecuteLlmResponseAsync(string npcId, string response, bool canSpeak, bool canEmote, string? interactorId = null)
    {
        if (string.IsNullOrWhiteSpace(response))
            return;

        // Store the interactor so give/other commands can use it for "player" resolution
        _currentInteractorId = interactorId;

        try
        {
            // Process goal markups [goal:type] or [goal:clear] - these don't produce visible actions
            await ProcessGoalMarkupsAsync(npcId, response);

            // Process plan/step markups [plan:...] or [step:done/skip] - these don't produce visible actions
            await ProcessPlanMarkupsAsync(npcId, response);

            // Collect all action elements with their positions for sequential execution
            var actions = new List<(int Position, string Type, string Content, int EndPosition)>();

            // Find all command markups [cmd:...]
            foreach (Match match in CommandMarkupPattern.Matches(response))
            {
                var command = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(command))
                {
                    // Check if command is forbidden
                    var cmdName = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
                    if (cmdName is not null && !ForbiddenCommands.Contains(cmdName))
                    {
                        actions.Add((match.Index, "command", command, match.Index + match.Length));
                    }
                }
            }

            // Find all emote patterns *emote* or [emote] (but not [cmd:...], [goal:...], [plan:...], [step:...])
            foreach (Match match in EmotePattern.Matches(response))
            {
                // Skip if this is a command, goal, plan, or step markup
                var fullMatch = match.Value;
                if (fullMatch.StartsWith("[cmd:", StringComparison.OrdinalIgnoreCase) ||
                    fullMatch.StartsWith("{cmd:", StringComparison.OrdinalIgnoreCase) ||
                    fullMatch.StartsWith("[goal:", StringComparison.OrdinalIgnoreCase) ||
                    fullMatch.StartsWith("{goal:", StringComparison.OrdinalIgnoreCase) ||
                    fullMatch.StartsWith("[plan:", StringComparison.OrdinalIgnoreCase) ||
                    fullMatch.StartsWith("{plan:", StringComparison.OrdinalIgnoreCase) ||
                    fullMatch.StartsWith("[step:", StringComparison.OrdinalIgnoreCase) ||
                    fullMatch.StartsWith("{step:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Get emote text: Groups[2] = *asterisks*, Groups[3] = [brackets]
                var emoteText = match.Groups[2].Success
                    ? match.Groups[2].Value.Trim()
                    : match.Groups[3].Value.Trim();

                if (!string.IsNullOrWhiteSpace(emoteText))
                {
                    actions.Add((match.Index, "emote", emoteText, match.Index + match.Length));
                }
            }

            // Sort actions by position
            actions.Sort((a, b) => a.Position.CompareTo(b.Position));

            // Remove overlapping actions (keep the first one at each position)
            var filteredActions = new List<(int Position, string Type, string Content, int EndPosition)>();
            var lastEndPos = 0;
            foreach (var action in actions)
            {
                if (action.Position >= lastEndPos)
                {
                    filteredActions.Add(action);
                    lastEndPos = action.EndPosition;
                }
            }

            // Create a version of response with markups removed for speech detection
            var cleanResponse = CommandMarkupPattern.Replace(response, " ").Trim();
            cleanResponse = GoalMarkupPattern.Replace(cleanResponse, " ").Trim();
            cleanResponse = PlanMarkupPattern.Replace(cleanResponse, " ").Trim();
            cleanResponse = StepActionPattern.Replace(cleanResponse, " ").Trim();

            // If no actions found, treat entire cleaned response as speech
            if (filteredActions.Count == 0)
            {
                if (canSpeak)
                {
                    var speech = CleanSpeech(cleanResponse.Trim());
                    if (!string.IsNullOrEmpty(speech))
                    {
                        await ExecuteAsync(npcId, $"say {TruncateSpeech(speech)}");
                    }
                }
                return;
            }

            // Execute actions sequentially with speech between them
            const int maxActions = 3;
            var actionsExecuted = 0;
            var currentPos = 0;

            foreach (var action in filteredActions)
            {
                if (actionsExecuted >= maxActions)
                    break;

                // Check for speech BEFORE this action
                if (action.Position > currentPos && canSpeak)
                {
                    var rawSpeech = response.Substring(currentPos, action.Position - currentPos);
                    // Remove any markup from the speech segment
                    rawSpeech = CommandMarkupPattern.Replace(rawSpeech, " ");
                    rawSpeech = GoalMarkupPattern.Replace(rawSpeech, " ");
                    rawSpeech = PlanMarkupPattern.Replace(rawSpeech, " ");
                    rawSpeech = StepActionPattern.Replace(rawSpeech, " ");
                    var speech = CleanSpeech(rawSpeech.Trim());
                    if (!string.IsNullOrEmpty(speech))
                    {
                        await ExecuteAsync(npcId, $"say {TruncateSpeech(speech)}");
                        actionsExecuted++;
                        if (actionsExecuted >= maxActions)
                            break;
                    }
                }

                // Execute the action
                if (action.Type == "command")
                {
                    if (await ExecuteAsync(npcId, action.Content))
                    {
                        actionsExecuted++;
                    }
                }
                else if (action.Type == "emote")
                {
                    // Check if the "emote" is actually speech wrapped in asterisks
                    if (canSpeak && LooksLikeSpeech(action.Content))
                    {
                        var speech = CleanSpeech(action.Content);
                        if (!string.IsNullOrEmpty(speech))
                        {
                            await ExecuteAsync(npcId, $"say {TruncateSpeech(speech)}");
                            actionsExecuted++;
                        }
                    }
                    else if (canEmote)
                    {
                        var emoteText = CleanEmote(action.Content);
                        if (!string.IsNullOrWhiteSpace(emoteText))
                        {
                            await ExecuteAsync(npcId, $"emote {emoteText}");
                            actionsExecuted++;
                        }
                    }
                }

                currentPos = action.EndPosition;
            }

            // Check for any remaining speech after the last action
            if (actionsExecuted < maxActions && currentPos < response.Length && canSpeak)
            {
                var remainingSpeech = response.Substring(currentPos);
                // Remove any markup from the speech segment
                remainingSpeech = CommandMarkupPattern.Replace(remainingSpeech, " ");
                remainingSpeech = GoalMarkupPattern.Replace(remainingSpeech, " ");
                remainingSpeech = PlanMarkupPattern.Replace(remainingSpeech, " ");
                remainingSpeech = StepActionPattern.Replace(remainingSpeech, " ");
                var speech = CleanSpeech(remainingSpeech.Trim());
                if (!string.IsNullOrEmpty(speech))
                {
                    await ExecuteAsync(npcId, $"say {TruncateSpeech(speech)}");
                }
            }
        }
        finally
        {
            // Clear the interactor context
            _currentInteractorId = null;
        }
    }

    /// <summary>
    /// Check if text inside emote markers (*...*) is actually speech.
    /// LLMs sometimes wrap speech in asterisks like *"Hello!"*
    /// </summary>
    private static bool LooksLikeSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();

        // If it starts with a quote, it's probably speech
        if (trimmed.StartsWith('"') || trimmed.StartsWith('"') || trimmed.StartsWith('\''))
            return true;

        // If it ends with a quote and contains mostly dialogue-like content
        if (trimmed.EndsWith('"') || trimmed.EndsWith('"') || trimmed.EndsWith('\''))
            return true;

        return false;
    }

    /// <summary>
    /// Clean and prepare speech text for the say command.
    /// Strips surrounding quotes and handles common LLM formatting issues.
    /// </summary>
    private static string CleanSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove leading/trailing quotes that LLMs often add
        var cleaned = text.Trim();

        // Strip surrounding double quotes
        if (cleaned.StartsWith('"') && cleaned.EndsWith('"') && cleaned.Length > 2)
        {
            cleaned = cleaned[1..^1].Trim();
        }
        else if (cleaned.StartsWith('"'))
        {
            cleaned = cleaned[1..].Trim();
        }
        else if (cleaned.EndsWith('"'))
        {
            cleaned = cleaned[..^1].Trim();
        }

        // Also handle smart quotes
        cleaned = cleaned.TrimStart('"', '"').TrimEnd('"', '"').Trim();

        // If it's just punctuation or empty, return empty
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.All(c => !char.IsLetterOrDigit(c)))
            return string.Empty;

        return cleaned;
    }

    /// <summary>
    /// Clean emote text - remove any inner asterisks or brackets the LLM may have added.
    /// </summary>
    private static string CleanEmote(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove asterisks and brackets that might be nested
        var cleaned = text.Replace("*", "").Replace("[", "").Replace("]", "").Trim();

        // If it's just punctuation or empty, return empty
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.All(c => !char.IsLetterOrDigit(c)))
            return string.Empty;

        return cleaned;
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

        // Clear failure reason before execution
        _lastFailureReason = null;

        var result = cmd switch
        {
            // Communication
            "say" => ExecuteSay(npcId, npcName, roomId, args, capabilities),
            "emote" or "me" => ExecuteEmote(npcId, npcName, roomId, args, capabilities),

            // Movement
            "go" => await ExecuteGoAsync(npcId, npcName, roomId, args, capabilities),
            "n" or "north" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "north" }, capabilities),
            "s" or "south" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "south" }, capabilities),
            "e" or "east" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "east" }, capabilities),
            "w" or "west" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "west" }, capabilities),
            "u" or "up" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "up" }, capabilities),
            "d" or "down" => await ExecuteGoAsync(npcId, npcName, roomId, new[] { "down" }, capabilities),

            // Item manipulation
            "get" or "take" => ExecuteGet(npcId, npcName, roomId, args, capabilities),
            "drop" => ExecuteDrop(npcId, npcName, roomId, args, capabilities),
            "give" => ExecuteGive(npcId, npcName, roomId, args, capabilities),

            // Equipment
            "equip" or "wear" or "wield" => ExecuteEquip(npcId, npcName, roomId, args, capabilities),
            "unequip" or "remove" => ExecuteUnequip(npcId, npcName, roomId, args, capabilities),

            // Combat
            "kill" or "attack" => ExecuteAttack(npcId, npcName, roomId, args, capabilities),
            "flee" or "retreat" => ExecuteFlee(npcId, npcName, roomId, capabilities),

            // Items
            "use" or "drink" or "eat" => ExecuteUse(npcId, npcName, roomId, args, capabilities),

            // Any other command - try as a local room command (well, shop, etc.)
            _ => await ExecuteLocalCommandAsync(npcId, npcName, roomId, cmd, args, capabilities)
        };

        // Log command execution
        _state.LlmDebugger?.LogCommand(npcId, command, result);

        // Record result for feedback to NPC on next turn (include failure reason if any)
        RecordCommandResult(npcId, cmd, args, result, _lastFailureReason);

        return result;
    }

    /// <summary>
    /// Sets a failure reason for command feedback.
    /// </summary>
    private bool Fail(string reason)
    {
        _lastFailureReason = reason;
        return false;
    }

    /// <summary>
    /// Sets a failure reason and returns a failed Task for async methods.
    /// </summary>
    private Task<bool> FailAsync(string reason)
    {
        _lastFailureReason = reason;
        return Task.FromResult(false);
    }

    /// <summary>
    /// Records a command result in the NPC's state for feedback on the next LLM call.
    /// </summary>
    private void RecordCommandResult(string npcId, string cmd, string[] args, bool success, string? reason = null)
    {
        var stateStore = _state.Objects?.GetStateStore(npcId);
        if (stateStore is null)
            return;

        // Build result description
        var argStr = args.Length > 0 ? string.Join(" ", args) : "";
        string resultStr;
        if (success)
        {
            resultStr = $"[OK] {cmd} {argStr}".Trim();
        }
        else
        {
            resultStr = reason is not null
                ? $"[FAILED] {cmd} {argStr} - {reason}".Trim()
                : $"[FAILED] {cmd} {argStr}".Trim();
        }

        // Get existing results and add new one
        var results = stateStore.Get<List<string>>(LastCommandResultsKey) ?? new List<string>();
        results.Add(resultStr);

        // Keep only last 3 results to avoid bloat
        if (results.Count > 3)
            results = results.Skip(results.Count - 3).ToList();

        stateStore.Set(LastCommandResultsKey, results);
    }

    /// <summary>
    /// Gets and clears the last command results for an NPC.
    /// Called by BuildNpcContextAsync to include feedback in the next prompt.
    /// </summary>
    public static List<string> GetAndClearCommandResults(IStateStore? stateStore)
    {
        if (stateStore is null)
            return new List<string>();

        var results = stateStore.Get<List<string>>(LastCommandResultsKey) ?? new List<string>();
        if (results.Count > 0)
        {
            // Clear after reading so each result is only shown once
            stateStore.Remove(LastCommandResultsKey);
        }
        return results;
    }

    /// <summary>
    /// Peeks at the last command results without clearing them.
    /// Used for building memory queries before full context retrieval.
    /// </summary>
    public static List<string> PeekCommandResults(IStateStore? stateStore)
    {
        if (stateStore is null)
            return new List<string>();

        return stateStore.Get<List<string>>(LastCommandResultsKey) ?? new List<string>();
    }

    /// <summary>
    /// Counts the number of consecutive failures from the end of the results list.
    /// Used for "blocked detection" - if too many failures, NPC should re-plan.
    /// </summary>
    public static int CountConsecutiveFailures(IReadOnlyList<string> results)
    {
        int count = 0;
        // Count from end backwards
        for (int i = results.Count - 1; i >= 0; i--)
        {
            if (results[i].StartsWith("[FAILED]", StringComparison.OrdinalIgnoreCase))
                count++;
            else
                break; // Stop at first success
        }
        return count;
    }

    /// <summary>
    /// Extracts a summary of failed commands for memory query context.
    /// Returns something like "Failed: go north, get water"
    /// </summary>
    public static string? BuildFailureSummary(IReadOnlyList<string> results)
    {
        var failures = results
            .Where(r => r.StartsWith("[FAILED]", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Replace("[FAILED]", "").Trim())
            .ToList();

        if (failures.Count == 0)
            return null;

        return $"Failed actions: {string.Join("; ", failures)}";
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
        {
            _state.LlmDebugger?.Log($"GO_FAIL: {npcId} - no CanWander capability");
            return FailAsync("cannot move");
        }

        if (args.Length == 0)
        {
            _state.LlmDebugger?.Log($"GO_FAIL: {npcId} - no direction specified");
            return FailAsync("no direction specified");
        }

        var direction = args[0].ToLowerInvariant();

        // Get current room
        var room = _state.Objects?.Get<IRoom>(roomId);
        if (room is null)
        {
            _state.LlmDebugger?.Log($"GO_FAIL: {npcId} - room '{roomId}' not found");
            return FailAsync("room not found");
        }

        // Find matching exit
        var exitKey = room.Exits.Keys.FirstOrDefault(k =>
            k.Equals(direction, StringComparison.OrdinalIgnoreCase));

        if (exitKey is null)
        {
            var availableExits = string.Join(", ", room.Exits.Keys);
            _state.LlmDebugger?.Log($"GO_FAIL: {npcId} - no exit '{direction}' in room (available: {availableExits})");
            return FailAsync($"no exit '{direction}' (valid: {availableExits})");
        }

        var destinationId = room.Exits[exitKey];

        // Load destination room if not already loaded
        var destRoom = _state.Objects?.Get<IRoom>(destinationId);
        if (destRoom is null)
        {
            // Try to load the room
            destRoom = _state.Objects?.LoadAsync<IRoom>(destinationId, _state).GetAwaiter().GetResult();
            if (destRoom is null)
            {
                _state.LlmDebugger?.Log($"GO_FAIL: {npcId} - destination '{destinationId}' failed to load");
                return FailAsync("destination blocked");
            }
        }

        // Process spawns for the destination room (NPCs activate rooms like players do)
        // This is safe because ProcessSpawnsAsync checks for existing instances globally
        _ = _state.ProcessSpawnsAsync(destRoom.Id, _clock);

        // Process spawns for any linked rooms (e.g., shop storage)
        if (destRoom is IHasLinkedRooms hasLinkedRooms)
        {
            foreach (var linkedRoomId in hasLinkedRooms.LinkedRooms)
            {
                var linkedRoom = _state.Objects?.LoadAsync<IRoom>(linkedRoomId, _state).GetAwaiter().GetResult();
                if (linkedRoom is not null)
                {
                    _ = _state.ProcessSpawnsAsync(linkedRoom.Id, _clock);
                }
            }
        }

        // Trigger departure event in current room
        _ = TriggerRoomEventAsync(new RoomEvent
        {
            Type = RoomEventType.Departure,
            ActorId = npcId,
            ActorName = npcName,
            Direction = direction
        }, roomId);

        // Send departure message to players in the room
        _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Emote, $"leaves {direction}.", roomId));

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

        // Send arrival message to players in the destination room
        var arrivalMsg = oppositeDir is not null
            ? $"arrives from the {oppositeDir}."
            : "arrives.";
        _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Emote, arrivalMsg, destinationId));

        return Task.FromResult(true);
    }

    private bool ExecuteGet(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanManipulateItems))
            return Fail("cannot manipulate items");

        if (args.Length == 0)
            return Fail("no item specified");

        var itemName = string.Join(" ", args);

        // Find item in room
        var itemId = FindItemInContainer(itemName, roomId);
        if (itemId is null)
            return Fail($"'{itemName}' not found in room");

        var item = _state.Objects?.Get<IItem>(itemId);
        if (item is null)
            return Fail("not a valid item");

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
            return Fail("cannot manipulate items");

        if (args.Length == 0)
            return Fail("no item specified");

        var itemName = string.Join(" ", args);

        // Find item in NPC inventory
        var itemId = FindItemInContainer(itemName, npcId);
        if (itemId is null)
            return Fail($"'{itemName}' not in inventory");

        var item = _state.Objects?.Get<IItem>(itemId);
        if (item is null)
            return Fail("not a valid item");

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
            return Fail("cannot attack");

        if (args.Length == 0)
            return Fail("no target specified");

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
            return Fail($"'{targetName}' not found in room");

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

    private bool ExecuteGive(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanManipulateItems))
            return false;

        // Parse "give <item> to <target>" or "give <target> <item>"
        if (args.Length < 2)
            return false;

        string itemName;
        string targetName;

        // Check for "to" separator
        var toIndex = Array.FindIndex(args, a => a.Equals("to", StringComparison.OrdinalIgnoreCase));
        if (toIndex > 0 && toIndex < args.Length - 1)
        {
            // "give <item> to <target>"
            itemName = string.Join(" ", args.Take(toIndex));
            targetName = string.Join(" ", args.Skip(toIndex + 1));
        }
        else
        {
            // "give <target> <item>" - last word is item, rest is target
            targetName = args[0];
            itemName = string.Join(" ", args.Skip(1));
        }

        // Find item in NPC inventory
        var itemId = FindItemInContainer(itemName, npcId);
        if (itemId is null)
            return false;

        var item = _state.Objects?.Get<IItem>(itemId);
        if (item is null)
            return false;

        // Find target in room
        var targetId = FindLivingInRoom(targetName.ToLowerInvariant(), roomId, npcId);
        if (targetId is null)
            return false;

        var target = _state.Objects?.Get<IMudObject>(targetId);
        if (target is null)
            return false;

        // Move item to target's inventory
        _state.Containers.Move(itemId, targetId);

        // Record event
        var itemDisplayName = item.ShortDescription;
        _state.EventLog.Record(roomId, $"{npcName} gives {itemDisplayName} to {target.Name}.");

        // Send message to room
        _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Emote, $"gives {itemDisplayName} to {target.Name}.", roomId));

        return true;
    }

    private bool ExecuteEquip(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
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

        var item = _state.Objects?.Get<IEquippable>(itemId);
        if (item is null)
            return false; // Not equippable

        // Check if something is already equipped in that slot
        var existingItemId = _state.Equipment.GetEquipped(npcId, item.Slot);
        if (existingItemId is not null)
        {
            var existingItem = _state.Objects?.Get<IEquippable>(existingItemId);
            if (existingItem is not null)
            {
                // Unequip existing item first
                var existingCtx = _state.CreateContext(existingItemId, _clock);
                existingItem.OnUnequip(npcId, existingCtx);
            }
        }

        // Equip the new item
        _state.Equipment.Equip(npcId, item.Slot, itemId);

        // Call OnEquip hook
        var itemCtx = _state.CreateContext(itemId, _clock);
        item.OnEquip(npcId, itemCtx);

        // Record event
        var itemDisplayName = item.ShortDescription;
        _state.EventLog.Record(roomId, $"{npcName} equips {itemDisplayName}.");

        // Send message to room
        _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Emote, $"equips {itemDisplayName}.", roomId));

        return true;
    }

    private bool ExecuteUnequip(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanManipulateItems))
            return false;

        if (args.Length == 0)
            return false;

        var slotOrItemName = string.Join(" ", args).ToLowerInvariant();

        // Try to parse as slot name
        if (Enum.TryParse<EquipmentSlot>(slotOrItemName, ignoreCase: true, out var slot))
        {
            var itemId = _state.Equipment.GetEquipped(npcId, slot);
            if (itemId is null)
                return false;

            var item = _state.Objects?.Get<IEquippable>(itemId);
            if (item is not null)
            {
                var itemCtx = _state.CreateContext(itemId, _clock);
                item.OnUnequip(npcId, itemCtx);
            }

            _state.Equipment.Unequip(npcId, slot);

            var itemDisplayName = item?.ShortDescription ?? itemId;
            _state.EventLog.Record(roomId, $"{npcName} removes {itemDisplayName}.");
            _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Emote, $"removes {itemDisplayName}.", roomId));

            return true;
        }

        // Try to find by item name in equipped items
        foreach (var s in Enum.GetValues<EquipmentSlot>())
        {
            var equippedId = _state.Equipment.GetEquipped(npcId, s);
            if (equippedId is null) continue;

            var equippedItem = _state.Objects?.Get<IEquippable>(equippedId);
            if (equippedItem is null) continue;

            if (equippedItem.Name.ToLowerInvariant().Contains(slotOrItemName) ||
                equippedItem.ShortDescription.ToLowerInvariant().Contains(slotOrItemName))
            {
                var itemCtx = _state.CreateContext(equippedId, _clock);
                equippedItem.OnUnequip(npcId, itemCtx);
                _state.Equipment.Unequip(npcId, s);

                var itemDisplayName = equippedItem.ShortDescription;
                _state.EventLog.Record(roomId, $"{npcName} removes {itemDisplayName}.");
                _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Emote, $"removes {itemDisplayName}.", roomId));

                return true;
            }
        }

        return false;
    }

    private bool ExecuteUse(string npcId, string npcName, string roomId, string[] args, NpcCapabilities capabilities)
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

        // Check if item is usable (has IUsable interface)
        if (item is not IUsable usable)
            return false;

        // Create context and use the item
        var itemCtx = _state.CreateContext(itemId, _clock);
        usable.OnUse(npcId, itemCtx);

        // Record event
        var itemDisplayName = item.ShortDescription;
        _state.EventLog.Record(roomId, $"{npcName} uses {itemDisplayName}.");

        // Send message to room
        _state.Messages.Enqueue(new MudMessage(npcId, null, MessageType.Emote, $"uses {itemDisplayName}.", roomId));

        return true;
    }

    private string? FindLivingInRoom(string name, string roomId, string excludeId)
    {
        var contents = _state.Containers.GetContents(roomId);

        foreach (var objId in contents)
        {
            if (objId == excludeId) continue;

            var obj = _state.Objects?.Get<IMudObject>(objId);
            if (obj is null) continue;

            // Check if it's a living entity
            if (obj is not ILiving living) continue;

            // Check name match
            if (obj.Name.ToLowerInvariant().Contains(name))
                return objId;

            // Check aliases (e.g., "barnaby" for shopkeeper)
            foreach (var alias in living.Aliases)
            {
                if (alias.ToLowerInvariant().Contains(name) ||
                    name.Contains(alias.ToLowerInvariant()))
                    return objId;
            }

            // Check for player names in session format
            if (objId.StartsWith("session:"))
            {
                var playerPart = objId.Split(':').LastOrDefault()?.ToLowerInvariant();
                if (playerPart is not null && playerPart.Contains(name))
                    return objId;
            }
        }

        // Fallback: if searching for "player" and we have a current interactor, use them
        // This handles LLM output like "give sword to player" when they mean the person they're responding to
        if (name == "player" && _currentInteractorId is not null)
        {
            // Verify the interactor is in the room
            if (contents.Contains(_currentInteractorId))
                return _currentInteractorId;
        }

        return null;
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
    /// Process goal markups in the LLM response.
    /// [goal:type] or [goal:type target] sets a goal with default importance.
    /// [goal:clear] clears all goals (except survival).
    /// [goal:clear type] clears a specific goal type.
    /// [goal:done type] marks a specific goal as complete (clears it).
    /// </summary>
    private async Task ProcessGoalMarkupsAsync(string npcId, string response)
    {
        var memorySystem = _state.MemorySystem;
        if (memorySystem is null)
            return;

        // Check for goal clear patterns
        var clearMatch = GoalClearPattern.Match(response);
        if (clearMatch.Success)
        {
            var clearContent = clearMatch.Groups[1].Value.Trim().ToLowerInvariant();

            // [goal:clear type] or [goal:done type] - clear specific goal type
            var clearParts = clearContent.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (clearParts.Length > 1)
            {
                var clearedGoalType = clearParts[1];
                await memorySystem.Goals.ClearAsync(npcId, clearedGoalType);
                _state.LlmDebugger?.LogGoalChange(npcId, "cleared", clearedGoalType);

                // Restore default goal if the cleared goal was the default
                await RestoreDefaultGoalIfNeededAsync(npcId, clearedGoalType);
            }
            else
            {
                // [goal:clear] - clear all goals except survival
                await memorySystem.Goals.ClearAllAsync(npcId, preserveSurvival: true);
                _state.LlmDebugger?.LogGoalChange(npcId, "cleared_all", "all");

                // Restore the default goal after clearing all
                await RestoreDefaultGoalIfNeededAsync(npcId, goalTypeCleared: null);
            }
            return;
        }

        // Check for goal set
        var match = GoalMarkupPattern.Match(response);
        if (!match.Success)
            return;

        var goalContent = match.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(goalContent))
            return;

        // Skip if this is a clear pattern (already handled above)
        if (goalContent.StartsWith("clear", StringComparison.OrdinalIgnoreCase) ||
            goalContent.StartsWith("done", StringComparison.OrdinalIgnoreCase) ||
            goalContent.StartsWith("complete", StringComparison.OrdinalIgnoreCase) ||
            goalContent.Equals("none", StringComparison.OrdinalIgnoreCase))
            return;

        // Parse goal: first word is type, rest is target (optional)
        var parts = goalContent.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var goalType = parts[0].ToLowerInvariant();
        var targetPlayer = parts.Length > 1 ? parts[1].Trim() : null;

        // "survive" is a drive, not a persisted goal.
        if (goalType == "survive")
            return;

        // Normalize "player" target to actual interactor if available
        if (targetPlayer?.Equals("player", StringComparison.OrdinalIgnoreCase) == true && _currentInteractorId is not null)
        {
            // Try to get player name from session
            var session = _state.Sessions.GetByPlayerId(_currentInteractorId);
            if (session?.PlayerName is not null)
                targetPlayer = session.PlayerName;
        }

        var goal = new NpcGoal(
            NpcId: npcId,
            GoalType: goalType,
            TargetPlayer: targetPlayer,
            Params: System.Text.Json.JsonDocument.Parse("{}"),
            Status: "active",
            Importance: GoalImportance.Default,  // LLM-set goals use default importance
            UpdatedAt: DateTimeOffset.UtcNow);

        await memorySystem.Goals.UpsertAsync(goal);
        _state.LlmDebugger?.LogGoalChange(npcId, "set", goalType, targetPlayer);
    }

    /// <summary>
    /// Restore the default goal for an NPC after a goal has been completed or cleared.
    /// Uses the NPC's IHasDefaultGoal interface to determine the default goal.
    /// If goalTypeCleared is specified, only restores if it matches the default goal type.
    /// </summary>
    private async Task RestoreDefaultGoalIfNeededAsync(string npcId, string? goalTypeCleared)
    {
        var memorySystem = _state.MemorySystem;
        if (memorySystem is null)
            return;

        // Get the NPC object to check if it implements IHasDefaultGoal
        var npc = _state.Objects?.Get<IMudObject>(npcId);
        if (npc is not IHasDefaultGoal hasDefaultGoal)
            return;

        var defaultGoalType = hasDefaultGoal.DefaultGoalType;
        if (string.IsNullOrWhiteSpace(defaultGoalType))
            return;

        // If a specific goal type was cleared, only restore if it matches the default
        if (goalTypeCleared is not null &&
            !goalTypeCleared.Equals(defaultGoalType, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Check if the default goal already exists (don't duplicate)
        var existingGoals = await memorySystem.Goals.GetAllAsync(npcId);
        if (existingGoals.Any(g => g.GoalType.Equals(defaultGoalType, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // Build goal params - include plan from template if available
        System.Text.Json.JsonDocument goalParams;
        var planTemplate = hasDefaultGoal.DefaultPlanTemplate;
        if (!string.IsNullOrWhiteSpace(planTemplate))
        {
            var plan = GoalPlan.FromSteps(planTemplate);
            goalParams = plan.ToParams();
        }
        else
        {
            goalParams = System.Text.Json.JsonDocument.Parse("{}");
        }

        // Restore the default goal
        var goal = new NpcGoal(
            NpcId: npcId,
            GoalType: defaultGoalType,
            TargetPlayer: hasDefaultGoal.DefaultGoalTarget,
            Params: goalParams,
            Status: "active",
            Importance: hasDefaultGoal.DefaultGoalImportance,
            UpdatedAt: DateTimeOffset.UtcNow);

        await memorySystem.Goals.UpsertAsync(goal);
        _state.LlmDebugger?.LogGoalChange(npcId, "restored_default", defaultGoalType, hasDefaultGoal.DefaultGoalTarget);
    }

    /// <summary>
    /// Process plan markups in the LLM response.
    /// [plan:step1|step2|step3] sets a plan for the highest priority goal.
    /// [plan:goalType:step1|step2|step3] sets a plan for a specific goal type.
    /// [step:done] completes the current step for the top goal.
    /// [step:goalType:done] completes the current step for a specific goal type.
    /// </summary>
    private async Task ProcessPlanMarkupsAsync(string npcId, string response)
    {
        var memorySystem = _state.MemorySystem;
        if (memorySystem is null)
            return;

        // Get all active goals
        var goals = await memorySystem.Goals.GetAllAsync(npcId);
        var activeGoals = goals.Where(g =>
            string.Equals(g.Status, "active", StringComparison.OrdinalIgnoreCase)).ToList();

        if (activeGoals.Count == 0)
            return;

        var topGoal = activeGoals.FirstOrDefault();

        // Check for plan set: [plan:step1|step2|step3] or [plan:goalType:step1|step2|step3]
        var planMatch = PlanMarkupPattern.Match(response);
        if (planMatch.Success)
        {
            var planContent = planMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(planContent))
            {
                // Parse optional goalType: "goalType:step1|step2|..." or just "step1|step2|..."
                var (targetGoalType, stepsContent) = ParseTargetedMarkup(planContent, '|');

                // Find the target goal
                var targetGoal = targetGoalType is not null
                    ? activeGoals.FirstOrDefault(g => string.Equals(g.GoalType, targetGoalType, StringComparison.OrdinalIgnoreCase))
                    : topGoal;

                if (targetGoal is not null && !string.IsNullOrWhiteSpace(stepsContent))
                {
                    var plan = GoalPlan.FromSteps(stepsContent);
                    if (plan.HasPlan)
                    {
                        var newParams = plan.ToParams(targetGoal.Params);
                        await memorySystem.Goals.UpdateParamsAsync(npcId, targetGoal.GoalType, newParams);
                        _state.LlmDebugger?.LogGoalChange(npcId, "plan_set", targetGoal.GoalType,
                            $"{plan.Steps.Count} steps");
                    }
                }
            }
        }

        // Check for step action: [step:done], [step:skip], [step:goalType:done], [step:goalType:skip]
        var stepMatch = StepActionPattern.Match(response);
        if (stepMatch.Success)
        {
            var stepContent = stepMatch.Groups[1].Value.Trim().ToLowerInvariant();

            // Parse optional goalType: "goalType:done" or just "done"
            var (targetGoalType, action) = ParseTargetedStepAction(stepContent);

            // Find the target goal
            var targetGoal = targetGoalType is not null
                ? activeGoals.FirstOrDefault(g => string.Equals(g.GoalType, targetGoalType, StringComparison.OrdinalIgnoreCase))
                : topGoal;

            if (targetGoal is null)
                return;

            var plan = GoalPlan.FromParams(targetGoal.Params);

            if (!plan.HasPlan)
                return;

            bool hasMoreSteps;
            string actionTaken;

            switch (action)
            {
                case "done":
                case "complete":
                    hasMoreSteps = plan.CompleteCurrentStep();
                    actionTaken = "step_done";
                    break;
                case "skip":
                case "next":
                    hasMoreSteps = plan.SkipCurrentStep();
                    actionTaken = "step_skip";
                    break;
                default:
                    return;
            }

            // Update the plan in the goal
            var newParams = plan.ToParams(targetGoal.Params);
            await memorySystem.Goals.UpdateParamsAsync(npcId, targetGoal.GoalType, newParams);

            if (plan.IsComplete)
            {
                _state.LlmDebugger?.LogGoalChange(npcId, "plan_complete", targetGoal.GoalType);
                // Goal plan is complete - mark goal as done
                await memorySystem.Goals.ClearAsync(npcId, targetGoal.GoalType);
                _state.LlmDebugger?.LogGoalChange(npcId, "cleared", targetGoal.GoalType);
                await RestoreDefaultGoalIfNeededAsync(npcId, targetGoal.GoalType);
            }
            else
            {
                _state.LlmDebugger?.LogGoalChange(npcId, actionTaken, targetGoal.GoalType,
                    plan.CurrentStepText ?? "unknown");
            }
        }
    }

    /// <summary>
    /// Parses targeted markup like "goalType:content" or just "content".
    /// For plans: checks if first segment before ':' is a goalType (doesn't contain pipe separator).
    /// </summary>
    private static (string? GoalType, string Content) ParseTargetedMarkup(string content, char stepsDelimiter)
    {
        // Check if content contains a goalType prefix: "goalType:step1|step2|..."
        // The goalType itself should NOT contain the steps delimiter (pipe)
        var colonIndex = content.IndexOf(':');
        if (colonIndex > 0)
        {
            var possibleGoalType = content.Substring(0, colonIndex);
            // If the part before ':' contains the steps delimiter, it's not a goalType
            if (!possibleGoalType.Contains(stepsDelimiter))
            {
                var stepsContent = content.Substring(colonIndex + 1);
                return (possibleGoalType, stepsContent);
            }
        }

        // No goalType prefix found, use content as-is
        return (null, content);
    }

    /// <summary>
    /// Parses targeted step action like "goalType:done" or just "done".
    /// </summary>
    private static (string? GoalType, string Action) ParseTargetedStepAction(string content)
    {
        var validActions = new HashSet<string> { "done", "complete", "skip", "next" };

        // Check if content is a simple action
        if (validActions.Contains(content))
            return (null, content);

        // Check for "goalType:action" format
        var colonIndex = content.LastIndexOf(':');
        if (colonIndex > 0)
        {
            var action = content.Substring(colonIndex + 1);
            if (validActions.Contains(action))
            {
                var goalType = content.Substring(0, colonIndex);
                return (goalType, action);
            }
        }

        // Unknown format, return as action (will be filtered by switch)
        return (null, content);
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

    /// <summary>
    /// Execute a local room command (like "draw" for well).
    /// </summary>
    private async Task<bool> ExecuteLocalCommandAsync(string npcId, string npcName, string roomId, string cmd, string[] args, NpcCapabilities capabilities)
    {
        if (!capabilities.HasFlag(NpcCapabilities.CanManipulateItems))
            return Fail("cannot manipulate items");

        // Get the room
        var room = _state.Objects?.Get<IRoom>(roomId);
        if (room is null)
            return Fail("not in a room");

        // Check if room has local commands
        if (room is not IHasCommands roomWithCommands)
            return Fail($"'{cmd}' not available here");

        // Find the matching command
        var localCmd = roomWithCommands.LocalCommands
            .FirstOrDefault(c => c.Name.Equals(cmd, StringComparison.OrdinalIgnoreCase) ||
                                  c.Aliases.Any(a => a.Equals(cmd, StringComparison.OrdinalIgnoreCase)));

        if (localCmd is null)
            return Fail($"'{cmd}' not available here");

        // Create context and execute
        try
        {
            var ctx = new MudContext(_state, _clock, _state.LlmService)
            {
                State = _state.Objects!.GetStateStore(roomId) ?? new DictionaryStateStore(),
                CurrentObjectId = roomId,
                RoomId = roomId
            };

            await roomWithCommands.HandleLocalCommandAsync(localCmd.Name, args, npcId, ctx);

            // Record the action
            _state.EventLog.Record(roomId, $"{npcName} uses the {localCmd.Name}.");

            return true;
        }
        catch (Exception ex)
        {
            _state.LlmDebugger?.Log($"LOCAL_CMD_FAIL: {npcId} - {cmd}: {ex.Message}");
            return Fail($"'{cmd}' failed");
        }
    }
}
