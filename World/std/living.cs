using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// Base class for all living beings (players, NPCs, monsters).
/// Manages HP via IStateStore for persistence across reloads.
/// Provides natural regeneration via heartbeat.
/// If the subclass implements ILlmNpc, automatic LLM event processing is provided.
/// </summary>
public abstract class LivingBase : MudObjectBase, ILiving, IOnLoad, IHeartbeat
{
    /// <summary>
    /// Pending LLM event to process on next heartbeat.
    /// Only one event is queued at a time to prevent spam.
    /// </summary>
    private RoomEvent? _pendingLlmEvent;

    /// <summary>
    /// Timestamp of last LLM response to prevent spam.
    /// </summary>
    private DateTime _lastLlmResponseTime = DateTime.MinValue;

    /// <summary>
    /// Whether we're currently processing an LLM request (prevent concurrent calls).
    /// </summary>
    private bool _isProcessingLlm;

    /// <summary>
    /// Engagement tracking - maps player ID to last interaction timestamp.
    /// Engaged players get immediate responses without needing to say the NPC's name.
    /// </summary>
    private readonly Dictionary<string, DateTime> _engagedWith = new();

    /// <summary>
    /// Cached context for property access.
    /// Set during OnLoad.
    /// </summary>
    protected IMudContext? Ctx { get; private set; }

    /// <summary>
    /// Current hit points. Stored in IStateStore for persistence.
    /// </summary>
    public int HP => Ctx?.State.Get<int>("hp") ?? 0;

    /// <summary>
    /// Maximum hit points. Override in derived classes.
    /// </summary>
    public virtual int MaxHP => 100;

    /// <summary>
    /// Detailed description shown when examining this living being.
    /// Can be overridden via state store with "description" key, or override in derived classes.
    /// </summary>
    public override string Description => GetDescription();

    /// <summary>
    /// Get the description, checking state store for override first.
    /// </summary>
    protected virtual string GetDescription()
    {
        // Check for state override first
        var stateDesc = Ctx?.State.Get<string>("description");
        if (!string.IsNullOrEmpty(stateDesc))
            return stateDesc;

        // Return default or class-defined description
        return GetDefaultDescription();
    }

    /// <summary>
    /// Default description when not overridden in state.
    /// Override this in derived classes instead of Description.
    /// </summary>
    protected virtual string GetDefaultDescription() => $"You see {Name}.";

    /// <summary>
    /// Whether this living is alive (HP > 0).
    /// </summary>
    public bool IsAlive => HP > 0;

    /// <summary>
    /// Heartbeat interval for regeneration.
    /// Override to customize regeneration rate.
    /// </summary>
    public virtual TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(1);

    /// <summary>
    /// Amount healed per heartbeat tick. Override to customize.
    /// </summary>
    protected virtual int RegenAmount => 1;

    /// <summary>
    /// Chance to wander each heartbeat (0-100). Set > 0 to enable wandering.
    /// Default is 0 (no wandering).
    /// </summary>
    public virtual int WanderChance => 0;

    /// <summary>
    /// Alternative names players can use to reference this living being.
    /// Override in derived classes to provide character names, roles, etc.
    /// Default returns the Name property as the only alias.
    /// </summary>
    public virtual IReadOnlyList<string> Aliases => new[] { Name };

    /// <summary>
    /// Brief description shown in room listings.
    /// Example: "a shopkeeper", "the goblin"
    /// Override to customize. Default adds "a" or "an" article to Name.
    /// </summary>
    public virtual string ShortDescription => AddArticle(Name);

    /// <summary>
    /// Add an indefinite article ("a" or "an") to a word.
    /// </summary>
    private static string AddArticle(string word)
    {
        if (string.IsNullOrEmpty(word))
            return word;

        // Use "an" before vowel sounds
        var first = char.ToLowerInvariant(word[0]);
        var article = first is 'a' or 'e' or 'i' or 'o' or 'u' ? "an" : "a";
        return $"{article} {word}";
    }

    #region Engagement Tracking

    /// <summary>
    /// Check if this NPC is currently engaged with a specific player.
    /// Engagement expires after EngagementTimeoutSeconds.
    /// </summary>
    protected bool IsEngagedWith(string playerId)
    {
        if (!_engagedWith.TryGetValue(playerId, out var lastInteraction))
            return false;

        var elapsed = (DateTime.UtcNow - lastInteraction).TotalSeconds;
        if (elapsed > EngagementTimeoutSeconds)
        {
            _engagedWith.Remove(playerId);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Engage this NPC with a player (called when NPC responds or is directly addressed).
    /// </summary>
    protected void EngageWith(string playerId)
    {
        _engagedWith[playerId] = DateTime.UtcNow;
    }

    /// <summary>
    /// Clear engagement with a player (called on departure).
    /// </summary>
    protected void DisengageFrom(string playerId)
    {
        _engagedWith.Remove(playerId);
    }

    /// <summary>
    /// Check if a speech message directly mentions this NPC by name or alias.
    /// </summary>
    protected bool IsSpeechDirectlyAddressed(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var msgLower = message.ToLowerInvariant();

        // Check if our name is mentioned
        if (msgLower.Contains(Name.ToLowerInvariant()))
            return true;

        // Check if any alias is mentioned
        foreach (var alias in Aliases)
        {
            if (msgLower.Contains(alias.ToLowerInvariant()))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if the NPC is alone with the speaker in the room (1:1 conversation).
    /// When only two living beings are present, speech is considered directed.
    /// </summary>
    protected bool IsAloneWithSpeaker(string speakerId, IMudContext ctx)
    {
        // Get our current room
        var myId = ctx.CurrentObjectId;
        if (string.IsNullOrEmpty(myId)) return false;

        var roomId = ctx.World.GetObjectLocation(myId);
        if (string.IsNullOrEmpty(roomId)) return false;

        // Get all objects in the room
        var contents = ctx.World.GetRoomContents(roomId);

        // Count living beings (ILiving) in the room
        int livingCount = 0;
        bool speakerPresent = false;
        bool weArePresent = false;

        foreach (var objId in contents)
        {
            var obj = ctx.World.GetObject<ILiving>(objId);
            if (obj is not null)
            {
                livingCount++;
                if (objId == speakerId) speakerPresent = true;
                if (objId == myId) weArePresent = true;
            }
        }

        // 1:1 conversation: exactly 2 living beings, both are us and the speaker
        return livingCount == 2 && speakerPresent && weArePresent;
    }

    #endregion

    /// <summary>
    /// Minimum time between LLM responses for non-speech events (in seconds).
    /// Override to customize. Default is 3 seconds.
    /// </summary>
    protected virtual double LlmCooldownSeconds => 3.0;

    /// <summary>
    /// Minimum time between speech responses (in seconds).
    /// Much shorter than general cooldown for responsive conversation.
    /// Override to customize. Default is 0.5 seconds.
    /// </summary>
    protected virtual double SpeechCooldownSeconds => 0.5;

    /// <summary>
    /// How long engagement lasts without interaction (seconds).
    /// Override to customize per-NPC. Default is 60 seconds.
    /// </summary>
    protected virtual double EngagementTimeoutSeconds => 60.0;

    /// <summary>
    /// The direction this living last came from.
    /// Used to avoid immediately returning to the previous room.
    /// </summary>
    protected string? LastCameFrom => Ctx?.State.Get<string>("last_came_from");

    /// <summary>
    /// Set the direction this living came from (for wander logic).
    /// </summary>
    protected void SetLastCameFrom(string? direction, IMudContext ctx)
    {
        if (direction is not null)
            ctx.State.Set("last_came_from", direction);
        else if (ctx.State.Has("last_came_from"))
            ctx.State.Remove("last_came_from");
    }

    /// <summary>
    /// Map of directions to their opposites for wander logic.
    /// </summary>
    private static readonly Dictionary<string, string> OppositeDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["north"] = "south",
        ["south"] = "north",
        ["east"] = "west",
        ["west"] = "east",
        ["up"] = "down",
        ["down"] = "up",
        ["northeast"] = "southwest",
        ["northwest"] = "southeast",
        ["southeast"] = "northwest",
        ["southwest"] = "northeast",
        ["n"] = "s",
        ["s"] = "n",
        ["e"] = "w",
        ["w"] = "e",
        ["ne"] = "sw",
        ["nw"] = "se",
        ["se"] = "nw",
        ["sw"] = "ne",
        ["u"] = "d",
        ["d"] = "u"
    };

    /// <summary>
    /// Get the opposite direction for wander logic.
    /// </summary>
    protected static string? GetOppositeDirection(string direction)
    {
        return OppositeDirections.TryGetValue(direction, out var opposite) ? opposite : null;
    }

    /// <summary>
    /// Called when the living is loaded or created.
    /// Initializes HP if not already set.
    /// </summary>
    public virtual void OnLoad(IMudContext ctx)
    {
        Ctx = ctx;

        // Initialize HP if not set (new instance)
        if (!HasStateKey(ctx, "hp"))
        {
            ctx.State.Set("hp", MaxHP);
        }

        // Initialize description if not set (for patchability)
        if (!HasStateKey(ctx, "description"))
        {
            ctx.State.Set("description", GetDefaultDescription());
        }
    }

    /// <summary>
    /// Called periodically for regeneration and other timed effects.
    /// Also processes pending LLM events for NPCs implementing ILlmNpc.
    /// </summary>
    public virtual void Heartbeat(IMudContext ctx)
    {
        Ctx = ctx;

        // Natural regeneration: heal if alive and not at max HP
        if (IsAlive && HP < MaxHP)
        {
            var healAmount = Math.Min(RegenAmount, MaxHP - HP);
            HealInternal(healAmount, ctx);
        }

        // Process pending LLM event (if this NPC implements ILlmNpc)
        ProcessPendingLlmEvent(ctx);

        // Try to wander if enabled
        TryWander(ctx);
    }

    /// <summary>
    /// Attempt to wander to an adjacent room based on WanderChance.
    /// Called each heartbeat. Direction least likely to go is where we came from.
    /// </summary>
    protected virtual async void TryWander(IMudContext ctx)
    {
        // Skip if wandering is disabled or we're dead
        if (WanderChance <= 0 || !IsAlive)
            return;

        // Skip if there's a pending LLM event (let NPC react first)
        if (HasPendingLlmEvent)
            return;

        // Roll against wander chance
        if (Random.Shared.Next(100) >= WanderChance)
            return;

        // Get our current location
        var myId = ctx.CurrentObjectId;
        if (myId is null)
            return;

        var roomId = ctx.World.GetObjectLocation(myId);
        if (roomId is null)
            return;

        var room = ctx.World.GetObject<IRoom>(roomId);
        if (room is null || room.Exits.Count == 0)
            return;

        // Build list of possible exits, weighted against direction we came from
        var exits = room.Exits.Keys.ToList();
        if (exits.Count == 0)
            return;

        // If we came from a direction, reduce chance of going back that way
        var lastFrom = LastCameFrom;
        string chosenDirection;

        if (lastFrom is not null && exits.Count > 1)
        {
            // 80% chance to pick a direction that's NOT where we came from
            if (Random.Shared.NextDouble() < 0.8)
            {
                // Filter out the direction we came from
                var otherExits = exits.Where(e => !e.Equals(lastFrom, StringComparison.OrdinalIgnoreCase)).ToList();
                if (otherExits.Count > 0)
                {
                    chosenDirection = otherExits[Random.Shared.Next(otherExits.Count)];
                }
                else
                {
                    // All exits lead back, just pick one
                    chosenDirection = exits[Random.Shared.Next(exits.Count)];
                }
            }
            else
            {
                // 20% chance to backtrack or pick any direction
                chosenDirection = exits[Random.Shared.Next(exits.Count)];
            }
        }
        else
        {
            // No prior direction, pick randomly
            chosenDirection = exits[Random.Shared.Next(exits.Count)];
        }

        // Remember the opposite direction (where we'll be coming from after moving)
        var opposite = GetOppositeDirection(chosenDirection);
        if (opposite is not null)
        {
            SetLastCameFrom(opposite, ctx);
        }

        // Execute the move command
        await ctx.ExecuteCommandAsync($"go {chosenDirection}");
    }

    /// <summary>
    /// Take damage from an attacker or environmental source.
    /// </summary>
    public virtual void TakeDamage(int amount, string? attackerId, IMudContext ctx)
    {
        Ctx = ctx;

        if (!IsAlive || amount <= 0)
            return;

        // Allow IOnDamage to modify the damage
        if (this is IOnDamage onDamage)
        {
            amount = onDamage.OnDamage(amount, attackerId, ctx);
        }

        // Apply damage
        var newHp = Math.Max(0, HP - amount);
        ctx.State.Set("hp", newHp);

        // Check for death
        if (newHp <= 0)
        {
            Die(attackerId, ctx);
        }
    }

    /// <summary>
    /// Heal this living by the specified amount.
    /// </summary>
    public virtual void Heal(int amount, IMudContext ctx)
    {
        Ctx = ctx;

        if (!IsAlive || amount <= 0)
            return;

        HealInternal(amount, ctx);
    }

    /// <summary>
    /// Internal healing logic, also called during regeneration.
    /// </summary>
    protected virtual void HealInternal(int amount, IMudContext ctx)
    {
        var newHp = Math.Min(MaxHP, HP + amount);
        ctx.State.Set("hp", newHp);

        // Notify via hook
        if (this is IOnHeal onHeal)
        {
            onHeal.OnHeal(amount, ctx);
        }
    }

    /// <summary>
    /// Called when HP reaches 0. Override to customize death behavior.
    /// </summary>
    public virtual void Die(string? killerId, IMudContext ctx)
    {
        Ctx = ctx;

        // Announce death
        ctx.Emote("collapses to the ground!");

        // Notify via hook
        if (this is IOnDeath onDeath)
        {
            onDeath.OnDeath(killerId, ctx);
        }
    }

    /// <summary>
    /// Fully restore HP to maximum.
    /// </summary>
    public virtual void FullHeal(IMudContext ctx)
    {
        Ctx = ctx;
        ctx.State.Set("hp", MaxHP);
    }

    /// <summary>
    /// Check if a state key exists.
    /// </summary>
    private static bool HasStateKey(IMudContext ctx, string key)
    {
        foreach (var k in ctx.State.Keys)
        {
            if (k == key) return true;
        }
        return false;
    }

    #region LLM NPC Support

    /// <summary>
    /// Whether there's a pending LLM event to process.
    /// Useful for subclasses to check before/after base.Heartbeat().
    /// </summary>
    protected bool HasPendingLlmEvent => _pendingLlmEvent is not null;

    /// <summary>
    /// Queue an LLM event for processing. Speech events are processed immediately
    /// if not on cooldown; other events wait for next heartbeat.
    /// Call this from OnRoomEventAsync in ILlmNpc implementations.
    /// </summary>
    /// <param name="event">The room event to queue.</param>
    /// <param name="ctx">The MUD context.</param>
    /// <returns>A task that completes when the event is queued (or processed for immediate events).</returns>
    protected async Task QueueLlmEvent(RoomEvent @event, IMudContext ctx)
    {
        // Don't respond if dead
        if (!IsAlive) return;

        // Don't respond if LLM is disabled
        if (!ctx.IsLlmEnabled) return;

        // Don't react to own actions
        if (@event.ActorId == ctx.CurrentObjectId) return;

        // Clear engagement when someone leaves the room
        if (@event.Type == RoomEventType.Departure)
        {
            DisengageFrom(@event.ActorId);
            // Still process the event so NPC might react (e.g., "waves goodbye")
        }

        // Check if this event is directed at us (speech, combat target, given item, etc.)
        var isDirectedAtUs = IsEventDirectedAtNpc(@event, ctx);

        // Check if we're on cooldown (directed events have shorter cooldown for responsive interaction)
        var timeSinceLastResponse = (DateTime.UtcNow - _lastLlmResponseTime).TotalSeconds;
        var cooldown = isDirectedAtUs ? SpeechCooldownSeconds : LlmCooldownSeconds;
        var isOnCooldown = timeSinceLastResponse < cooldown;

        // Directed events get immediate processing if not on cooldown and not already processing
        if (isDirectedAtUs && !isOnCooldown && !_isProcessingLlm)
        {
            // Process immediately for faster response
            await ProcessLlmEventNow(@event, ctx);
        }
        else if (!isOnCooldown)
        {
            // Queue other events for heartbeat processing
            _pendingLlmEvent = @event;
        }
        // If on cooldown, silently ignore the event
    }

    /// <summary>
    /// Check if a room event is directed at this NPC (speech, combat, item given, etc.)
    /// These events warrant immediate response with shorter cooldown.
    /// </summary>
    private bool IsEventDirectedAtNpc(RoomEvent @event, IMudContext ctx)
    {
        // Speech: check for 1:1 conversation, direct address, OR active engagement
        if (@event.Type == RoomEventType.Speech)
        {
            // 1:1 conversation: only NPC and speaker in room - treat as directed
            if (IsAloneWithSpeaker(@event.ActorId, ctx))
                return true;

            // Direct address: message contains our name or alias
            if (IsSpeechDirectlyAddressed(@event.Message))
                return true;

            // Active engagement: we're in conversation with this speaker
            if (IsEngagedWith(@event.ActorId))
                return true;

            // Otherwise this is ambient chatter - queue for heartbeat (rare reaction)
            return false;
        }

        // Check if we're the target of the event
        if (!string.IsNullOrEmpty(@event.Target))
        {
            var myId = ctx.CurrentObjectId ?? "";
            var myName = Name.ToLowerInvariant();
            var target = @event.Target.ToLowerInvariant();

            // Direct ID match or name match
            if (target.Contains(myName) || myId.Equals(@event.Target, StringComparison.OrdinalIgnoreCase))
                return true;

            // Check if target matches any of our aliases (e.g., "barnaby" for shopkeeper)
            foreach (var alias in Aliases)
            {
                if (target.Contains(alias.ToLowerInvariant()))
                    return true;
            }
        }

        // Combat and ItemGiven events with us as target are definitely directed
        if (@event.Type == RoomEventType.Combat || @event.Type == RoomEventType.ItemGiven)
        {
            // Already checked Target above, but these event types are inherently directed
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get the reaction instructions for the LLM prompt.
    /// Override to provide character-specific instructions.
    /// </summary>
    /// <param name="event">The event to react to.</param>
    /// <returns>Instructions for how the NPC should react.</returns>
    protected virtual string GetLlmReactionInstructions(RoomEvent @event)
    {
        // When someone speaks TO us, we MUST respond with speech
        if (@event.Type == RoomEventType.Speech && this is ILlmNpc npc && npc.Capabilities.HasFlag(NpcCapabilities.CanSpeak))
        {
            var hasItems = npc.Capabilities.HasFlag(NpcCapabilities.CanManipulateItems);
            if (hasItems)
            {
                return "Someone spoke to you. You MUST reply with SPEECH in quotes - do NOT use emotes! " +
                       "CRITICAL: If they ask for an item, you MUST include [cmd:give <item> to player] in your response. " +
                       "Saying 'here you go' or nodding does NOT transfer items - ONLY the [cmd:give] command works! " +
                       "Example: \"Sure, here it is!\" [cmd:give sword to player]";
            }
            return "Someone spoke to you. You MUST reply with SPEECH in quotes - do NOT use emotes! Give ONE short response.";
        }

        // When someone gives us an item, we might want to give it back or react
        if (@event.Type == RoomEventType.ItemGiven && this is ILlmNpc itemNpc && itemNpc.Capabilities.HasFlag(NpcCapabilities.CanManipulateItems))
        {
            return $"Someone gave you an item ({@event.Message}). You now have it in your inventory. " +
                   $"If you want to give it back or give them something else, you MUST use [cmd:give <item> to player]. " +
                   $"Emotes like *hands back the item* do NOT actually transfer items! " +
                   $"Reply with speech and optionally a command.";
        }

        // For other events, allow emotes or no reaction
        return $"React to this event as {Name}. Respond with exactly ONE short emote wrapped in asterisks (e.g. *looks up*). You may choose not to react at all.";
    }

    /// <summary>
    /// Process any pending LLM event during heartbeat.
    /// Called automatically if this class implements ILlmNpc.
    /// </summary>
    protected virtual async void ProcessPendingLlmEvent(IMudContext ctx)
    {
        // Only process if we implement ILlmNpc
        if (this is not ILlmNpc) return;
        if (_pendingLlmEvent is null || !IsAlive || !ctx.IsLlmEnabled) return;

        // Check cooldown
        var timeSinceLastResponse = (DateTime.UtcNow - _lastLlmResponseTime).TotalSeconds;
        if (timeSinceLastResponse < LlmCooldownSeconds) return;

        var eventToProcess = _pendingLlmEvent;
        _pendingLlmEvent = null;

        await ProcessLlmEventNow(eventToProcess, ctx);
    }

    /// <summary>
    /// Process an LLM event immediately (used for speech and heartbeat processing).
    /// </summary>
    private async Task ProcessLlmEventNow(RoomEvent eventToProcess, IMudContext ctx)
    {
        if (this is not ILlmNpc llmNpc) return;
        if (_isProcessingLlm) return; // Prevent concurrent LLM calls

        _isProcessingLlm = true;
        try
        {
            // Build full environmental context (pass 'this' as ILiving)
            var npcContext = ctx.BuildNpcContext(this);
            var environmentDesc = npcContext.BuildEnvironmentDescription();
            var actionInstructions = npcContext.BuildActionInstructions();
            var reactionInstructions = GetLlmReactionInstructions(eventToProcess);

            var userPrompt = $"{environmentDesc}\n\n{actionInstructions}\n\n[Event: {eventToProcess.Description}]\n\n{reactionInstructions}";

            var response = await ctx.LlmCompleteAsync(llmNpc.SystemPrompt, userPrompt);
            if (!string.IsNullOrEmpty(response))
            {
                // Check if the NPC decided not to react
                var lower = response.ToLowerInvariant();
                if (lower.Contains("no reaction") || lower.Contains("ignores") || lower.Contains("doesn't react"))
                    return;

                // Update cooldown timestamp
                _lastLlmResponseTime = DateTime.UtcNow;

                // Engage with the speaker for follow-up conversation
                EngageWith(eventToProcess.ActorId);

                // Parse and execute the response, passing the event actor as interactor
                await ctx.ExecuteLlmResponseAsync(response,
                    canSpeak: llmNpc.Capabilities.HasFlag(NpcCapabilities.CanSpeak),
                    canEmote: llmNpc.Capabilities.HasFlag(NpcCapabilities.CanEmote),
                    interactorId: eventToProcess.ActorId);
            }
        }
        finally
        {
            _isProcessingLlm = false;
        }
    }

    #endregion

    #region System Prompt Builder

    /// <summary>
    /// Identity for the system prompt. Defaults to Name.
    /// Override for custom identity (e.g., "a cunning goblin warrior").
    /// </summary>
    protected virtual string NpcIdentity => Name;

    /// <summary>
    /// Physical description and nature. Override to describe the NPC.
    /// Example: "A domestic cat with soft fur and keen senses."
    /// </summary>
    protected virtual string? NpcNature => null;

    /// <summary>
    /// How the NPC communicates. Override to define speech patterns.
    /// Example: "Broken grammar. Third person sometimes."
    /// </summary>
    protected virtual string? NpcCommunicationStyle => null;

    /// <summary>
    /// Personality traits. Override to define character.
    /// Example: "Curious, independent, easily spooked."
    /// </summary>
    protected virtual string? NpcPersonality => null;

    /// <summary>
    /// Example responses. Override to show ideal output format.
    /// Example: "\"What you want, pinkskin?\" or \"*hisses*\""
    /// </summary>
    protected virtual string? NpcExamples => null;

    /// <summary>
    /// Additional character-specific rules.
    /// Example: "NEVER use modern language."
    /// </summary>
    protected virtual string? NpcExtraRules => null;

    /// <summary>
    /// Build a complete system prompt for this NPC.
    /// Uses the virtual properties to construct a consistent prompt.
    /// NPCs can use this in their ILlmNpc.SystemPrompt implementation.
    /// </summary>
    protected string BuildSystemPrompt()
    {
        // Get capabilities if this implements ILlmNpc
        var capabilities = (this is ILlmNpc llmNpc)
            ? llmNpc.Capabilities
            : NpcCapabilities.Humanoid;

        var canSpeak = capabilities.HasFlag(NpcCapabilities.CanSpeak);
        var canEmote = capabilities.HasFlag(NpcCapabilities.CanEmote);

        var sb = new System.Text.StringBuilder();

        // Identity
        sb.AppendLine($"You are {NpcIdentity} in a fantasy MUD. You ARE {NpcIdentity}, not an AI.");
        sb.AppendLine();

        // Nature
        if (!string.IsNullOrWhiteSpace(NpcNature))
        {
            sb.AppendLine($"Your Nature: {NpcNature}");
            sb.AppendLine();
        }

        // Communication capabilities and style
        sb.AppendLine("How You Communicate:");
        sb.AppendLine("- NEVER use first person (I, me, my). Always use third person.");
        if (canEmote)
        {
            sb.AppendLine("- For actions/emotes: wrap in asterisks, START with a third-person VERB ending in 's'");
            sb.AppendLine("  CORRECT: *smiles warmly* or *waves at the customer* or *looks around*");
            sb.AppendLine("  WRONG: *nice greeting* or *friendly wave* or *I smile*");
            sb.AppendLine("  The first word MUST be an action verb like: smiles, nods, waves, looks, gestures, points");
        }
        if (canSpeak)
        {
            sb.AppendLine("- For speech: write the dialogue in quotes. You can speak multiple sentences.");
            sb.AppendLine("  CORRECT: \"Hello there! Welcome to my shop.\"");
            sb.AppendLine("  WRONG: I say hello, *says hello*");
        }
        if (!canSpeak)
        {
            sb.AppendLine("- You CANNOT speak human language");
        }
        if (!string.IsNullOrWhiteSpace(NpcCommunicationStyle))
        {
            sb.AppendLine($"- {NpcCommunicationStyle}");
        }
        sb.AppendLine();

        // Personality
        if (!string.IsNullOrWhiteSpace(NpcPersonality))
        {
            sb.AppendLine($"Personality: {NpcPersonality}");
            sb.AppendLine();
        }

        // Examples
        if (!string.IsNullOrWhiteSpace(NpcExamples))
        {
            sb.AppendLine($"Example: {NpcExamples}");
            sb.AppendLine();
        }

        // Command markup section (if NPC can take actions)
        var canManipulate = capabilities.HasFlag(NpcCapabilities.CanManipulateItems);
        var canWander = capabilities.HasFlag(NpcCapabilities.CanWander);
        var canAttack = capabilities.HasFlag(NpcCapabilities.CanAttack);
        var canFlee = capabilities.HasFlag(NpcCapabilities.CanFlee);

        // Always show command markup section for NPCs with any action capability
        if (canSpeak || canEmote || canManipulate || canWander || canAttack)
        {
            sb.AppendLine("Command Actions:");
            sb.AppendLine("- To ACTUALLY perform game actions, you MUST use [cmd:command] markup");
            sb.AppendLine("- IMPORTANT: Emotes like *hands you the sword* only DESCRIBE actions - they do NOT execute them!");
            sb.AppendLine("- To give an item, you MUST use [cmd:give item to player] - describing giving does nothing");
            sb.AppendLine("- Example: \"Here you go!\" [cmd:give sword to player]");
            sb.AppendLine();
            sb.AppendLine("Available commands:");
            if (canSpeak)
            {
                sb.AppendLine("  [cmd:say <message>] - speak aloud");
            }
            if (canEmote)
            {
                sb.AppendLine("  [cmd:emote <action>] - perform an action (e.g., [cmd:emote waves warmly])");
            }
            if (canManipulate)
            {
                sb.AppendLine("  [cmd:get <item>] - pick up an item from the room");
                sb.AppendLine("  [cmd:drop <item>] - drop an item");
                sb.AppendLine("  [cmd:give <item> to <person>] - give item to someone");
                sb.AppendLine("  [cmd:equip <item>] - equip/wield an item");
                sb.AppendLine("  [cmd:unequip <item or slot>] - remove equipped item");
                sb.AppendLine("  [cmd:use <item>] - use/consume an item");
            }
            if (canWander)
            {
                sb.AppendLine("  [cmd:go <direction>] - move to another room");
            }
            if (canAttack)
            {
                sb.AppendLine("  [cmd:attack <target>] - attack someone");
            }
            if (canFlee)
            {
                sb.AppendLine("  [cmd:flee] - flee from combat");
            }
            sb.AppendLine();
        }

        // Core rules (always included)
        sb.AppendLine("Rules:");
        sb.AppendLine("- NEVER break character");
        sb.AppendLine("- Respond with exactly ONE short action per event - one emote OR one sentence");
        sb.AppendLine("- Do NOT chain multiple actions together");
        sb.AppendLine("- When asked to give/get/drop items, you MUST use [cmd:...] - emotes alone do nothing");
        if (!string.IsNullOrWhiteSpace(NpcExtraRules))
        {
            sb.AppendLine($"- {NpcExtraRules}");
        }

        return sb.ToString().TrimEnd();
    }

    #endregion
}
