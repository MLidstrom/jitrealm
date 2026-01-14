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
    /// Timestamp of last autonomous goal "think" tick to prevent background spam.
    /// </summary>
    private DateTime _lastGoalThinkTime = DateTime.MinValue;

    /// <summary>
    /// Whether we're currently processing an LLM request (prevent concurrent calls).
    /// </summary>
    private bool _isProcessingLlm;

    /// <summary>
    /// Engagement tracking - maps a stable player identifier (account/name) to last interaction timestamp.
    /// Engaged players get immediate responses without needing to say the NPC's name.
    /// </summary>
    private readonly Dictionary<string, DateTime> _engagedWith = new();

    /// <summary>
    /// Timestamp of last direct interaction with any player.
    /// Used to pause wandering for a configurable duration after interaction.
    /// </summary>
    private DateTime _lastInteractionTime = DateTime.MinValue;

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
    /// How long (seconds) to pause wandering after direct player interaction.
    /// Prevents NPCs from wandering away during conversation.
    /// Default is 300 seconds (5 minutes).
    /// </summary>
    protected virtual double WanderPauseAfterInteractionSeconds => 300.0;

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
    protected bool IsEngagedWith(string playerKey)
    {
        if (!_engagedWith.TryGetValue(playerKey, out var lastInteraction))
            return false;

        var elapsed = (DateTime.UtcNow - lastInteraction).TotalSeconds;
        if (elapsed > EngagementTimeoutSeconds)
        {
            _engagedWith.Remove(playerKey);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Engage this NPC with a player (called when NPC responds or is directly addressed).
    /// </summary>
    protected void EngageWith(string playerKey)
    {
        _engagedWith[playerKey] = DateTime.UtcNow;
    }

    /// <summary>
    /// Clear engagement with a player (called on departure).
    /// </summary>
    protected void DisengageFrom(string playerKey)
    {
        _engagedWith.Remove(playerKey);
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
    /// Override to customize. Default is 0 (no artificial delay - LLM latency is sufficient).
    /// </summary>
    protected virtual double LlmCooldownSeconds => 0;

    /// <summary>
    /// Minimum time between speech responses (in seconds).
    /// Override to customize. Default is 0 (no artificial delay - LLM latency is sufficient).
    /// </summary>
    protected virtual double SpeechCooldownSeconds => 0;

    /// <summary>
    /// How long engagement lasts without interaction (seconds).
    /// Override to customize per-NPC. Default is 60 seconds.
    /// </summary>
    protected virtual double EngagementTimeoutSeconds => 60.0;

    /// <summary>
    /// Enable autonomous goal pursuit (background "think" ticks).
    /// This is separate from reacting to room events.
    /// </summary>
    protected virtual bool AutonomousGoalPursuitEnabled => true;

    /// <summary>
    /// How often (seconds) this NPC may take an autonomous goal step when idle.
    /// Keep this relatively slow for scalability.
    /// </summary>
    protected virtual double GoalThinkIntervalSeconds => 10.0;

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

    #region Key Location Wandering

    /// <summary>
    /// Current destination room name for key location wandering.
    /// </summary>
    protected string? WanderDestination => Ctx?.State.Get<string>("_wander_destination");

    /// <summary>
    /// Unix timestamp when the NPC should leave the current location.
    /// </summary>
    protected long WanderDwellUntil => Ctx?.State.Get<long>("_wander_dwell_until") ?? 0;

    /// <summary>
    /// Set the current wander destination.
    /// </summary>
    protected void SetWanderDestination(string? destination, IMudContext ctx)
    {
        if (destination is not null)
            ctx.State.Set("_wander_destination", destination);
        else if (ctx.State.Has("_wander_destination"))
            ctx.State.Remove("_wander_destination");
    }

    /// <summary>
    /// Set when the NPC should leave the current location.
    /// </summary>
    protected void SetWanderDwellUntil(long unixTimestamp, IMudContext ctx)
    {
        if (unixTimestamp > 0)
            ctx.State.Set("_wander_dwell_until", unixTimestamp);
        else if (ctx.State.Has("_wander_dwell_until"))
            ctx.State.Remove("_wander_dwell_until");
    }

    /// <summary>
    /// Check if the NPC is currently at a key location (fuzzy match on room name).
    /// </summary>
    protected bool IsAtKeyLocation(IReadOnlyList<string> keyLocations, IRoom currentRoom)
    {
        var roomName = currentRoom.Name?.ToLowerInvariant() ?? "";

        foreach (var loc in keyLocations)
        {
            var locLower = loc.ToLowerInvariant();
            if (roomName.Contains(locLower) || locLower.Contains(roomName))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Pick the next key location to visit.
    /// </summary>
    protected string? PickNextKeyLocation(IReadOnlyList<string> keyLocations, IRoom currentRoom, bool randomize)
    {
        if (keyLocations.Count == 0)
            return null;

        // Filter out current location
        var roomName = currentRoom.Name?.ToLowerInvariant() ?? "";
        var available = keyLocations
            .Where(loc => !roomName.Contains(loc.ToLowerInvariant()) &&
                         !loc.ToLowerInvariant().Contains(roomName))
            .ToList();

        if (available.Count == 0)
        {
            // All locations match current room, just pick any
            available = keyLocations.ToList();
        }

        if (randomize)
        {
            return available[Random.Shared.Next(available.Count)];
        }
        else
        {
            // Sequential: find current index and get next
            var currentIdx = -1;
            var currentLoc = keyLocations
                .Select((loc, idx) => (loc, idx))
                .FirstOrDefault(x => roomName.Contains(x.loc.ToLowerInvariant()) ||
                                     x.loc.ToLowerInvariant().Contains(roomName));

            if (currentLoc.loc != null)
                currentIdx = currentLoc.idx;

            var nextIdx = (currentIdx + 1) % keyLocations.Count;
            return keyLocations[nextIdx];
        }
    }

    /// <summary>
    /// Find a room ID by name (fuzzy match).
    /// </summary>
    protected string? FindRoomIdByName(string roomName, IMudContext ctx)
    {
        var targetLower = roomName.ToLowerInvariant();

        foreach (var objId in ctx.World.ListObjectIds())
        {
            var room = ctx.World.GetObject<IRoom>(objId);
            if (room is null)
                continue;

            var name = room.Name?.ToLowerInvariant() ?? "";
            if (name.Contains(targetLower) ||
                objId.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
            {
                return objId;
            }
        }

        return null;
    }

    /// <summary>
    /// Try to pick a destination based on the current plan step.
    /// Returns the key location that best matches the step, or null if no match.
    /// </summary>
    private string? TryPickPlanBasedDestination(NpcGoal? goal, IReadOnlyList<string> locations, IRoom currentRoom, IMudContext ctx)
    {
        if (goal is null)
            return null;

        var plan = GoalPlan.FromParams(goal.Params);
        if (!plan.HasPlan || plan.IsComplete)
            return null;

        var currentStep = plan.CurrentStepText;
        if (string.IsNullOrEmpty(currentStep))
            return null;

        var stepLower = currentStep.ToLowerInvariant();
        var currentRoomName = currentRoom.Name?.ToLowerInvariant() ?? "";

        // Try to find a key location that matches the current step
        foreach (var location in locations)
        {
            var locLower = location.ToLowerInvariant();

            // Skip if we're already at this location
            if (currentRoomName.Contains(locLower) || locLower.Contains(currentRoomName))
                continue;

            // Check if the step mentions this location
            var locWords = locLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .ToList();

            if (locWords.Any(word => stepLower.Contains(word)))
            {
                ctx.LogDebug($"WANDER: plan step '{currentStep}' matches location '{location}'");
                return location;
            }

            // Also try to find the room and check its aliases
            var roomId = FindRoomIdByName(location, ctx);
            if (roomId is not null)
            {
                var targetRoom = ctx.World.GetObject<IRoom>(roomId);
                if (targetRoom is not null && StepMatchesRoom(stepLower, targetRoom))
                {
                    ctx.LogDebug($"WANDER: plan step '{currentStep}' matches room '{targetRoom.Name}' (via aliases)");
                    return location;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Auto-complete a plan step if it matches the location the NPC finished dwelling at.
    /// Called when dwell time expires - the NPC has had time to complete activities at this location.
    /// </summary>
    protected async Task TryAutoCompletePlanStep(IMudContext ctx, NpcGoal goal, IRoom room)
    {
        var plan = GoalPlan.FromParams(goal.Params);
        if (!plan.HasPlan || plan.IsComplete)
            return;

        var currentStep = plan.CurrentStepText;
        if (string.IsNullOrEmpty(currentStep))
            return;

        var stepLower = currentStep.ToLowerInvariant();

        // Check if the step mentions the room name or any of its aliases
        if (!StepMatchesRoom(stepLower, room))
            return;

        // Auto-complete this step
        var npcId = ctx.CurrentObjectId ?? "unknown";
        ctx.LogDebug($"PLAN: {npcId} - auto-completing step '{currentStep}' (completed activities at '{room.Name}')");

        await ctx.AdvanceGoalStepAsync(goal.GoalType);
    }

    /// <summary>
    /// Check if a plan step mentions a room (by name or aliases).
    /// </summary>
    private static bool StepMatchesRoom(string stepLower, IRoom room)
    {
        // Collect all location words: room name + all aliases
        var locationTerms = new List<string>();

        // Add room name words
        var nameLower = room.Name?.ToLowerInvariant() ?? "";
        locationTerms.AddRange(nameLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2));

        // Add all aliases (lowercase)
        foreach (var alias in room.Aliases)
        {
            var aliasLower = alias.ToLowerInvariant();
            locationTerms.AddRange(aliasLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2));
        }

        // Check if step contains any location term
        return locationTerms.Any(term => stepLower.Contains(term));
    }

    #endregion

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

        // Apply survival goal and any default goals
        ApplyDefaultNeedsIfNeeded(ctx);
        ApplyDefaultGoalsIfNeeded(ctx);
    }

    /// <summary>
    /// Apply default needs/drives (all living entities).
    /// Survive is always level 1. Additional needs from IHasDefaultNeeds are also applied.
    /// </summary>
    private async void ApplyDefaultNeedsIfNeeded(IMudContext ctx)
    {
        try
        {
            // Ensure survive need exists at level 1 (all living entities).
            await ctx.SetNeedAsync("survive", level: NeedLevel.Survival, status: "active");

            // Apply additional needs from IHasDefaultNeeds if implemented
            if (this is IHasDefaultNeeds hasNeeds)
            {
                // Get existing needs to check what's already set
                var existingNeeds = await ctx.GetAllNeedsAsync();
                var existingTypes = new HashSet<string>(existingNeeds.Select(n => n.NeedType), StringComparer.OrdinalIgnoreCase);

                foreach (var (needType, level) in hasNeeds.DefaultNeeds)
                {
                    // Skip survive (already handled above) and skip if already exists
                    if (string.Equals(needType, "survive", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!existingTypes.Contains(needType))
                    {
                        await ctx.SetNeedAsync(needType, level, "active");
                    }
                }
            }
        }
        catch
        {
            // Silently ignore errors (need store may not be available)
        }
    }

    /// <summary>
    /// Apply default goals from IHasDefaultGoal.
    /// Note: "survive" is a drive (always-on priority), not a persisted goal.
    /// </summary>
    private async void ApplyDefaultGoalsIfNeeded(IMudContext ctx)
    {
        var myId = ctx.CurrentObjectId;
        if (string.IsNullOrEmpty(myId))
            return;

        try
        {
            // Get existing goals to avoid duplicates
            var existingGoals = await ctx.GetAllGoalsAsync();
            var existingTypes = new HashSet<string>(existingGoals.Select(g => g.GoalType));

            // Apply default goal from IHasDefaultGoal if not present
            if (this is IHasDefaultGoal hasDefault &&
                !string.IsNullOrWhiteSpace(hasDefault.DefaultGoalType) &&
                !existingTypes.Contains(hasDefault.DefaultGoalType))
            {
                await ctx.SetGoalAsync(
                    hasDefault.DefaultGoalType,
                    hasDefault.DefaultGoalTarget,
                    "active",
                    hasDefault.DefaultGoalImportance);
            }
        }
        catch
        {
            // Silently ignore errors (goal store may not be available)
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

        // Autonomous goal pursuit tick (best-effort, bounded)
        ProcessAutonomousGoalThink(ctx);

        // Try to wander if enabled
        TryWander(ctx);
    }

    /// <summary>
    /// Autonomous goal pursuit: when idle, periodically call the LLM to take one step toward goals.
    /// This is intentionally conservative and rate-limited.
    /// </summary>
    private async void ProcessAutonomousGoalThink(IMudContext ctx)
    {
        try
        {
            if (!AutonomousGoalPursuitEnabled)
                return;

            if (this is not ILlmNpc llmNpc)
                return;

            if (!IsAlive)
                return;

            if (!ctx.IsLlmEnabled)
                return;

            // Don't compete with event reactions
            if (_pendingLlmEvent is not null)
                return;

            if (_isProcessingLlm)
                return;

            // Rate-limit background thinking
            var sinceThink = (DateTime.UtcNow - _lastGoalThinkTime).TotalSeconds;
            if (sinceThink < GoalThinkIntervalSeconds)
                return;

            // Fetch goals (if memory/goals system is unavailable, this will return empty)
            var goals = await ctx.GetAllGoalsAsync();

            // Choose a goal to pursue, or pursue the survive drive if in danger.
            // We can't directly query CombatScheduler from sandboxed world access,
            // so treat "danger" as primarily low HP for goal selection.
            var isHurt = MaxHP > 0 && HP * 100 / MaxHP <= 50;

            NpcGoal? goalToPursue = null;
            if (isHurt)
            {
                // Survive is a drive, not a persisted goal. If hurt, prioritize survival behavior even with no goals.
                goalToPursue = goals.FirstOrDefault(g => string.Equals(g.Status, "active", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                goalToPursue = goals.FirstOrDefault(g =>
                    string.Equals(g.Status, "active", StringComparison.OrdinalIgnoreCase));
            }

            // Phase 4: Need-to-goal derivation when no active goals exist
            if (!isHurt && goalToPursue is null)
            {
                // Get all needs and derive a goal from the top need (lowest level = highest priority)
                var needs = await ctx.GetAllNeedsAsync();
                var topNeed = needs.FirstOrDefault(n =>
                    !string.Equals(n.NeedType, "survive", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(n.Status, "active", StringComparison.OrdinalIgnoreCase));

                if (topNeed is null)
                    return;

                // Derive goal type from need (check IHasNeedGoalMapping first, then use convention)
                string? goalType = null;
                string? planTemplate = null;

                if (this is IHasNeedGoalMapping mapping)
                {
                    goalType = mapping.GetGoalForNeed(topNeed.NeedType);
                    if (!string.IsNullOrWhiteSpace(goalType))
                    {
                        planTemplate = mapping.GetPlanTemplateForGoal(goalType);
                    }
                }

                // Convention: need type becomes goal type (e.g., "hunt" -> "hunt", "tend_farm" -> "tend_farm")
                if (string.IsNullOrWhiteSpace(goalType))
                {
                    goalType = topNeed.NeedType;
                }

                // Create the derived goal
                await ctx.SetGoalAsync(goalType, targetPlayer: null, status: "active", importance: GoalImportance.Default);

                // If we have a plan template, we could set it here but let's let the LLM do that
                // The NPC will see they have a goal with no plan and create one on the next tick

                // Continue with the newly created goal
                goalToPursue = await ctx.GetGoalAsync();
                if (goalToPursue is null)
                    return;
            }

            _lastGoalThinkTime = DateTime.UtcNow;

            // Build context with semantic memory recall (if enabled).
            var goalText = isHurt
                ? "drive: survive"
                : string.IsNullOrWhiteSpace(goalToPursue!.TargetPlayer)
                    ? $"{goalToPursue.GoalType} ({goalToPursue.Status})"
                    : $"{goalToPursue.GoalType} -> {goalToPursue.TargetPlayer} ({goalToPursue.Status})";

            // Phase 1b & 1c: Peek at last action results for memory query and blocked detection
            var npcId = ctx.CurrentObjectId ?? string.Empty;
            var npcStateStore = ctx.World.GetStateStore(npcId);
            var recentResults = NpcCommandExecutor.PeekCommandResults(npcStateStore);
            var consecutiveFailures = NpcCommandExecutor.CountConsecutiveFailures(recentResults);
            var failureSummary = NpcCommandExecutor.BuildFailureSummary(recentResults);
            var isBlocked = consecutiveFailures >= 2;

            // Check if goal has a plan
            GoalPlan? currentPlan = null;
            string planInstructions = "";
            string? suggestedAction = null; // Phase 5: pathing suggestion
            if (!isHurt && goalToPursue is not null)
            {
                currentPlan = GoalPlan.FromParams(goalToPursue.Params);

                // If no plan exists, try to apply a template from IHasDefaultNeeds
                if (!currentPlan.HasPlan && this is IHasDefaultNeeds hasNeeds)
                {
                    var planTemplate = hasNeeds.GetPlanTemplateForGoal(goalToPursue.GoalType);
                    if (!string.IsNullOrWhiteSpace(planTemplate))
                    {
                        // Apply the template using SetGoalPlanAsync (preserves other goal params)
                        await ctx.SetGoalPlanAsync(goalToPursue.GoalType, planTemplate);

                        // Re-fetch to get the updated plan
                        goalToPursue = await ctx.GetGoalAsync();
                        if (goalToPursue is not null)
                        {
                            currentPlan = GoalPlan.FromParams(goalToPursue.Params);
                        }
                    }
                }

                // Phase 3: Try deterministic evaluators before calling LLM
                // Auto-advance steps that are clearly complete or blocked
                if (goalToPursue is not null && currentPlan.HasPlan && !currentPlan.IsComplete && !isBlocked)
                {
                    var stepText = currentPlan.CurrentStepText ?? "";
                    var evaluation = ctx.EvaluateGoalStep(goalToPursue, stepText);

                    if (evaluation is not null)
                    {
                        switch (evaluation.Result)
                        {
                            case StepEvaluationResult.Complete:
                                // Step is complete - auto-advance
                                ctx.Trace(TraceCategory.Step, $"COMPLETE: \"{stepText}\" - {evaluation.Reason ?? "done"}");
                                await ctx.AdvanceGoalStepAsync(goalToPursue.GoalType);

                                // Re-fetch the plan to see if there are more steps
                                var updatedGoal = await ctx.GetGoalAsync();
                                if (updatedGoal is null)
                                {
                                    // Goal was cleared (plan complete), we're done
                                    return;
                                }
                                currentPlan = GoalPlan.FromParams(updatedGoal.Params);
                                goalToPursue = updatedGoal;
                                break;

                            case StepEvaluationResult.Blocked:
                                // Step is blocked - skip it
                                ctx.Trace(TraceCategory.Step, $"BLOCKED: \"{stepText}\" - {evaluation.Reason ?? "cannot proceed"}");
                                await ctx.SkipGoalStepAsync(goalToPursue.GoalType);

                                // Re-fetch the plan
                                var updatedGoal2 = await ctx.GetGoalAsync();
                                if (updatedGoal2 is null)
                                {
                                    return;
                                }
                                currentPlan = GoalPlan.FromParams(updatedGoal2.Params);
                                goalToPursue = updatedGoal2;
                                break;

                            case StepEvaluationResult.InProgress:
                                // If we have a pathing suggestion, execute it directly (no LLM needed)
                                if (!string.IsNullOrEmpty(evaluation.SuggestedAction) &&
                                    evaluation.SuggestedAction.StartsWith("[cmd:go "))
                                {
                                    // Extract and execute the go command directly
                                    var cmdMatch = System.Text.RegularExpressions.Regex.Match(
                                        evaluation.SuggestedAction, @"\[cmd:go\s+(\w+)\]");
                                    if (cmdMatch.Success)
                                    {
                                        var direction = cmdMatch.Groups[1].Value;
                                        ctx.Trace(TraceCategory.Path, $"AUTO-MOVE: go {direction} (step: \"{stepText}\")");
                                        await ctx.ExecuteCommandAsync($"go {direction}");
                                        return; // Done for this tick, re-evaluate next heartbeat
                                    }
                                }
                                // Fallback: pass suggestion to LLM
                                suggestedAction = evaluation.SuggestedAction;
                                break;
                        }
                    }
                }

                // Phase 1c: If blocked (2+ consecutive failures), force re-plan even if we have a plan
                if (isBlocked)
                {
                    planInstructions =
                        "- BLOCKED: Your last 2+ attempts FAILED. You MUST create a NEW PLAN.\n" +
                        "- Analyze WHY the actions failed and choose a DIFFERENT approach.\n" +
                        "- Format: [plan:step1|step2|step3] (NOT [cmd:plan:...] - plans are separate from commands!)\n" +
                        "- Output the [plan:...] markup with an optional brief emote. Nothing else.\n";
                }
                else if (currentPlan.HasPlan && !currentPlan.IsComplete)
                {
                    var step = currentPlan.CurrentStepText ?? "next step";
                    // Phase 5: Include pathing suggestion if available
                    var pathingHint = !string.IsNullOrEmpty(suggestedAction)
                        ? $"- RECOMMENDED ACTION: {suggestedAction}\n"
                        : "";
                    // Tell NPCs with key locations that movement is automatic
                    var movementNote = this is IHasKeyLocations
                        ? "- IMPORTANT: Movement between locations is AUTOMATIC - do NOT use [cmd:go]. Just emote or speak naturally.\n"
                        : "";
                    planInstructions =
                        $"- Your current plan step is: \"{step}\" ({currentPlan.Progress})\n" +
                        pathingHint +
                        movementNote +
                        "- Focus on completing THIS step, then use [step:done] to advance.\n" +
                        "- If this step is blocked, use [step:skip] to move on.\n";
                }
                else if (!currentPlan.HasPlan)
                {
                    // Check if NPC has a suggested plan template
                    // First try IHasDefaultGoal, then IHasNeedGoalMapping for derived goals
                    var planTemplate = (this as IHasDefaultGoal)?.DefaultPlanTemplate;
                    if (string.IsNullOrWhiteSpace(planTemplate) && this is IHasNeedGoalMapping needMapping && goalToPursue is not null)
                    {
                        planTemplate = needMapping.GetPlanTemplateForGoal(goalToPursue.GoalType);
                    }
                    if (!string.IsNullOrWhiteSpace(planTemplate))
                    {
                        planInstructions =
                            "- IMPORTANT: You have NO PLAN. Your task now is to CREATE A PLAN using [plan:...] markup.\n" +
                            "- Format: [plan:step1|step2|step3] (NOT [cmd:plan:...] - plans are separate from commands!)\n" +
                            $"- SUGGESTED TEMPLATE for your goal: [plan:{planTemplate}]\n" +
                            "- Adapt this template to the current situation, or create your own plan.\n" +
                            "- Output the [plan:...] markup with an optional brief emote. Nothing else.\n";
                    }
                    else
                    {
                        planInstructions =
                            "- IMPORTANT: You have NO PLAN. Your task now is to CREATE A PLAN using [plan:...] markup.\n" +
                            "- Format: [plan:step1|step2|step3] (NOT [cmd:plan:...] - plans are separate from commands!)\n" +
                            "- Example for farmer: [plan:visit shop|buy supplies|return to farm|tend crops]\n" +
                            "- Output the [plan:...] markup with an optional brief emote. Nothing else.\n";
                    }
                }
            }

            // Phase 1b: Include failure summary in memory query for semantic recall
            // This helps retrieve memories about "how we solved similar problems before"
            var memoryQuery = $"Goal: {goalText}. NPC: {Name}. Room: {ctx.World.GetObjectLocation(npcId)}";
            if (!string.IsNullOrEmpty(failureSummary))
            {
                memoryQuery += $" {failureSummary}";
            }

            var npcContext = await ctx.BuildNpcContextAsync(
                this,
                focalPlayerName: isHurt ? null : goalToPursue!.TargetPlayer,
                memoryQueryText: memoryQuery);

            var environmentDesc = npcContext.BuildEnvironmentDescription();
            var actionInstructions = npcContext.BuildActionInstructions();

            // NPCs with key locations have automatic movement, so don't suggest [cmd:go]
            var cmdHint = this is IHasKeyLocations
                ? "- Use [cmd:...] for actions (give, get, say) but NOT for movement (automatic).\n"
                : "- Prefer real game actions using [cmd:...] markup.\n";
            var pursueInstructions =
                $"[Autonomous drive/goal pursuit]\n" +
                $"- Current priority: {goalText}\n" +
                planInstructions +
                "- Take ONE concrete step toward this priority.\n" +
                cmdHint +
                "- If a command FAILED, make a [plan:step1|step2|...] to achieve it.\n" +
                "- If the goal is complete, use [goal:done <type>] or [goal:clear <type>].\n" +
                "- If you need to set a new goal, use [goal:<type> <optional target>].\n" +
                "- If players are present, keep it natural and non-spammy (often a brief emote is enough).";

            var userPrompt = $"{environmentDesc}\n\n{actionInstructions}\n\n{pursueInstructions}";

            ctx.Trace(TraceCategory.Llm, $"CALLING LLM for goal: {goalText}");
            var response = await ctx.LlmCompleteAsync(llmNpc.SystemPrompt, userPrompt);
            if (string.IsNullOrWhiteSpace(response))
            {
                ctx.Trace(TraceCategory.Llm, "RESPONSE: (empty)");
                return;
            }

            ctx.Trace(TraceCategory.Llm, $"RESPONSE: {response.Replace("\n", " ").Substring(0, Math.Min(100, response.Length))}...");

            // Treat as a normal NPC response: execute bounded commands then optional speech/emote.
            _lastLlmResponseTime = DateTime.UtcNow;
            await ctx.ExecuteLlmResponseAsync(response,
                canSpeak: llmNpc.Capabilities.HasFlag(NpcCapabilities.CanSpeak),
                canEmote: llmNpc.Capabilities.HasFlag(NpcCapabilities.CanEmote),
                interactorId: null);
        }
        catch
        {
            // Best-effort only; never crash heartbeat
        }
    }

    /// <summary>
    /// Attempt to wander to an adjacent room based on WanderChance.
    /// If NPC implements IHasKeyLocations, wanders between key locations using PATHING_D.
    /// Otherwise, uses random wandering with backtrack avoidance.
    /// </summary>
    protected virtual async void TryWander(IMudContext ctx)
    {
        // Skip if wandering is disabled or we're dead
        if (WanderChance <= 0 || !IsAlive)
            return;

        // Skip if there's a pending LLM event (let NPC react first)
        if (HasPendingLlmEvent)
            return;

        // Skip if recently interacted with a player (pause wandering during conversation)
        var timeSinceInteraction = (DateTime.UtcNow - _lastInteractionTime).TotalSeconds;
        if (timeSinceInteraction < WanderPauseAfterInteractionSeconds)
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

        // Check if this NPC uses key location wandering
        if (this is IHasKeyLocations keyLocNpc)
        {
            await TryKeyLocationWander(ctx, keyLocNpc, room, roomId);
            return;
        }

        // Fall back to random wandering
        await TryRandomWander(ctx, room);
    }

    /// <summary>
    /// Key location wandering: travel between designated locations, dwell 5-10 min at each.
    /// Uses PATHING_D for navigation.
    /// </summary>
    private async Task TryKeyLocationWander(IMudContext ctx, IHasKeyLocations keyLocNpc, IRoom room, string roomId)
    {
        var npcId = ctx.CurrentObjectId ?? "unknown";

        // Get current goal to check for goal-specific locations
        var currentGoal = await ctx.GetGoalAsync();
        var goalType = currentGoal?.GoalType;

        // Determine active locations (goal-specific or default)
        var locations = (goalType != null ? keyLocNpc.GetLocationsForGoal(goalType) : null)
                        ?? keyLocNpc.DefaultKeyLocations;

        if (locations.Count == 0)
        {
            ctx.LogDebug($"WANDER: {npcId} - no key locations, random wander");
            await TryRandomWander(ctx, room);
            return;
        }

        var now = ctx.World.Now.ToUnixTimeSeconds();

        // Check if we're at a key location
        if (IsAtKeyLocation(locations, room))
        {
            // Check if we have a dwell time set
            var dwellUntil = WanderDwellUntil;
            if (dwellUntil == 0)
            {
                // Just arrived - set dwell time
                var (minSec, maxSec) = keyLocNpc.DwellDuration;
                var dwellSeconds = Random.Shared.Next(minSec, maxSec + 1);
                SetWanderDwellUntil(now + dwellSeconds, ctx);
                SetWanderDestination(null, ctx); // Clear destination, we've arrived
                ctx.LogDebug($"WANDER: {npcId} - arrived at '{room.Name}', dwelling {dwellSeconds}s");
                return; // Stay here
            }

            if (now < dwellUntil)
            {
                // Still dwelling, stay here (don't log every tick - too spammy)
                return;
            }

            // Dwell time expired - try to complete plan step for this location
            if (currentGoal != null)
            {
                await TryAutoCompletePlanStep(ctx, currentGoal, room);
            }

            // Re-fetch goal after potential plan step completion (goal may have updated params)
            currentGoal = await ctx.GetGoalAsync();

            // Pick next location - prefer plan-based, fall back to random
            var nextLoc = TryPickPlanBasedDestination(currentGoal, locations, room, ctx);
            if (nextLoc is null)
            {
                nextLoc = PickNextKeyLocation(locations, room, keyLocNpc.RandomizeOrder);
            }
            if (nextLoc != null)
            {
                SetWanderDestination(nextLoc, ctx);
                SetWanderDwellUntil(0, ctx); // Clear dwell time
                ctx.LogDebug($"WANDER: {npcId} - dwell expired at '{room.Name}', heading to '{nextLoc}'");
            }
        }

        // Check if we have a destination
        var destination = WanderDestination;
        if (string.IsNullOrEmpty(destination))
        {
            // First, try to pick a destination based on current plan step
            destination = TryPickPlanBasedDestination(currentGoal, locations, room, ctx);

            // If no plan-based destination, pick randomly from key locations
            if (destination is null)
            {
                destination = PickNextKeyLocation(locations, room, keyLocNpc.RandomizeOrder);
            }

            if (destination != null)
            {
                SetWanderDestination(destination, ctx);
                ctx.LogDebug($"WANDER: {npcId} - picked destination '{destination}' (from '{room.Name}')");
            }
            else
            {
                ctx.LogDebug($"WANDER: {npcId} - no destination available, random wander");
                await TryRandomWander(ctx, room);
                return;
            }
        }

        // Try to find the destination room and get path via PATHING_D
        var destRoomId = FindRoomIdByName(destination, ctx);
        if (destRoomId is null)
        {
            ctx.LogDebug($"WANDER: {npcId} - can't find room '{destination}', random wander");
            SetWanderDestination(null, ctx);
            await TryRandomWander(ctx, room);
            return;
        }

        // Get pathing daemon
        var pathingD = ctx.World.GetDaemon<IPathingDaemon>("PATHING_D");
        if (pathingD is null)
        {
            ctx.LogDebug($"WANDER: {npcId} - no PATHING_D, random wander");
            await TryRandomWander(ctx, room);
            return;
        }

        // Get next direction toward destination
        var nextDir = pathingD.GetNextDirection(roomId, destRoomId);
        if (nextDir is null)
        {
            // No path found - might be already there or unreachable
            var path = pathingD.FindPath(roomId, destRoomId);
            if (path.Found && path.Directions.Count == 0)
            {
                // We're at the destination
                SetWanderDestination(null, ctx);
                var (minSec, maxSec) = keyLocNpc.DwellDuration;
                var dwellSeconds = Random.Shared.Next(minSec, maxSec + 1);
                SetWanderDwellUntil(now + dwellSeconds, ctx);
                ctx.LogDebug($"WANDER: {npcId} - reached '{destination}', dwelling {dwellSeconds}s");
                return;
            }

            ctx.LogDebug($"WANDER: {npcId} - no path to '{destination}', random wander");
            SetWanderDestination(null, ctx);
            await TryRandomWander(ctx, room);
            return;
        }

        // Remember the opposite direction (where we'll be coming from after moving)
        var opposite = GetOppositeDirection(nextDir);
        if (opposite is not null)
        {
            SetLastCameFrom(opposite, ctx);
        }

        ctx.LogDebug($"WANDER: {npcId} - go {nextDir} toward '{destination}'");

        // Execute the move command
        await ctx.ExecuteCommandAsync($"go {nextDir}");
    }

    /// <summary>
    /// Random wandering: pick a random adjacent room, avoiding backtracking.
    /// Hidden exits are excluded - NPCs don't know about secret passages.
    /// </summary>
    private async Task TryRandomWander(IMudContext ctx, IRoom room)
    {
        // Filter out hidden exits - NPCs shouldn't use secret passages
        var exits = room.Exits.Keys.Where(e => !room.HiddenExits.Contains(e)).ToList();
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
    /// Queue an LLM event for heartbeat processing.
    /// Call this from OnRoomEventAsync in ILlmNpc implementations.
    /// </summary>
    /// <param name="event">The room event to queue.</param>
    /// <param name="ctx">The MUD context.</param>
    /// <returns>A task that completes when the event is queued.</returns>
#pragma warning disable CS1998 // Async method without await - async signature for concurrent access
    protected async Task QueueLlmEvent(RoomEvent @event, IMudContext ctx)
#pragma warning restore CS1998
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
            DisengageFrom(@event.ActorName);
            // Still process the event so NPC might react (e.g., "waves goodbye")
        }

        // Check if this event is directed at us (speech, combat target, given item, etc.)
        var isDirectedAtUs = IsEventDirectedAtNpc(@event, ctx);

        // Arrivals are semi-directed (greetings)
        var isArrival = @event.Type == RoomEventType.Arrival;

        // Mark event with priority info for heartbeat processing
        // Directed events and arrivals get priority processing with shorter cooldowns
        var priority = isDirectedAtUs ? EventPriority.Directed
                     : isArrival ? EventPriority.Arrival
                     : EventPriority.Normal;

        // Queue for heartbeat processing (replaces any previous pending event)
        // Heartbeat will check cooldown based on priority
        _pendingLlmEvent = @event;
        _pendingEventPriority = priority;
    }

    /// <summary>
    /// Priority levels for queued LLM events.
    /// </summary>
    private enum EventPriority
    {
        Normal,     // General events - use LlmCooldownSeconds
        Arrival,    // Player arrivals - use 1.0 second cooldown
        Directed    // Direct speech/combat - use SpeechCooldownSeconds
    }

    /// <summary>
    /// Priority of the pending LLM event.
    /// </summary>
    private EventPriority _pendingEventPriority = EventPriority.Normal;

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
            if (IsEngagedWith(@event.ActorName))
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
            var speechContent = @event.Message ?? "";
            var lower = speechContent.ToLowerInvariant();

            // Detect if this is a question
            var isQuestion = lower.Contains("?") || lower.Contains("who") || lower.Contains("what") ||
                            lower.Contains("where") || lower.Contains("why") || lower.Contains("how") ||
                            lower.Contains("your name") || lower.Contains("are you");

            if (isQuestion)
            {
                var itemNote = hasItems
                    ? " If they ask for an item, include [cmd:give <item> to player]."
                    : "";
                return $"QUESTION ASKED: \"{speechContent}\" — ANSWER THIS QUESTION DIRECTLY in speech (quotes). " +
                       $"Do NOT give a generic greeting. Respond to the specific question.{itemNote}";
            }

            if (hasItems)
            {
                return $"They said: \"{speechContent}\". Reply with SPEECH in quotes. Respond to what they said. " +
                       "If asking for an item, use [cmd:give <item> to player].";
            }
            return $"They said: \"{speechContent}\". Reply with SPEECH in quotes. Respond to what they said, not with a generic greeting.";
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
        if (_pendingLlmEvent is null) return;
        if (!IsAlive) return;
        if (!ctx.IsLlmEnabled) return;

        // Check cooldown based on event priority (default is 0 - no artificial delay)
        var timeSinceLastResponse = (DateTime.UtcNow - _lastLlmResponseTime).TotalSeconds;
        var cooldown = _pendingEventPriority switch
        {
            EventPriority.Directed => SpeechCooldownSeconds,
            EventPriority.Arrival => SpeechCooldownSeconds, // Use same cooldown as speech
            _ => LlmCooldownSeconds
        };

        if (cooldown > 0 && timeSinceLastResponse < cooldown)
        {
            return;
        }

        var eventToProcess = _pendingLlmEvent;
        _pendingLlmEvent = null;
        _pendingEventPriority = EventPriority.Normal;

        await ProcessLlmEventNow(eventToProcess, ctx);
    }

    /// <summary>
    /// Process an LLM event immediately (used for speech and heartbeat processing).
    /// </summary>
    private async Task ProcessLlmEventNow(RoomEvent eventToProcess, IMudContext ctx)
    {
        if (this is not ILlmNpc llmNpc) return;
        if (_isProcessingLlm) return;

        _isProcessingLlm = true;
        try
        {
            // Build full environmental context (pass 'this' as ILiving)
            var memoryQuery = $"{eventToProcess.Description}\n{eventToProcess.Message}".Trim();
            var npcContext = await ctx.BuildNpcContextAsync(
                this,
                focalPlayerName: eventToProcess.ActorName,
                memoryQueryText: memoryQuery);
            var environmentDesc = npcContext.BuildEnvironmentDescription();
            var actionInstructions = npcContext.BuildActionInstructions();
            var reactionInstructions = GetLlmReactionInstructions(eventToProcess);

            var userPrompt = $"{environmentDesc}\n\n{actionInstructions}\n\n[Event: {eventToProcess.Description}]\n\n{reactionInstructions}";

            ctx.Trace(TraceCategory.Event, $"REACTING to: {eventToProcess.Description}");
            var response = await ctx.LlmCompleteAsync(llmNpc.SystemPrompt, userPrompt);

            if (!string.IsNullOrEmpty(response))
            {
                // Check if the NPC decided not to react
                var lower = response.ToLowerInvariant();
                if (lower.Contains("no reaction") || lower.Contains("ignores") || lower.Contains("doesn't react"))
                {
                    ctx.Trace(TraceCategory.Event, "IGNORED event (no reaction)");
                    return;
                }

                ctx.Trace(TraceCategory.Llm, $"RESPONSE: {response.Replace("\n", " ").Substring(0, Math.Min(100, response.Length))}...");

                // Update cooldown timestamp
                _lastLlmResponseTime = DateTime.UtcNow;

                // Engage with the speaker for follow-up conversation
                EngageWith(eventToProcess.ActorName);

                // Pause wandering after direct interaction
                _lastInteractionTime = DateTime.UtcNow;

                // Parse and execute the response, passing the event actor as interactor
                await ctx.ExecuteLlmResponseAsync(response,
                    canSpeak: llmNpc.Capabilities.HasFlag(NpcCapabilities.CanSpeak),
                    canEmote: llmNpc.Capabilities.HasFlag(NpcCapabilities.CanEmote),
                    interactorId: eventToProcess.ActorId);
            }
        }
        catch
        {
            // Silently ignore LLM errors to avoid spamming console
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
            sb.AppendLine("  NEVER emote 'to self/myself/himself' - that's meaningless. Just *nods* not *nods to self*");
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
            sb.AppendLine("- CRITICAL: Emotes ONLY describe - they do NOT execute actions!");
            // NPCs with key locations have automatic movement
            if (this is IHasKeyLocations)
            {
                sb.AppendLine("- *walks toward the shop* is fine for flavor - movement is AUTOMATIC between your locations");
                sb.AppendLine("- Do NOT use [cmd:go] - you will travel automatically to locations matching your plan");
            }
            else
            {
                sb.AppendLine("- *walks toward the shop* does NOTHING. To move: [cmd:go north]");
            }
            sb.AppendLine("- *hands you the sword* does NOTHING. To give: [cmd:give sword to player]");
            sb.AppendLine("- NEVER emote giving, attacking, or other game actions (except movement for key-location NPCs)");
            sb.AppendLine("- Only emote things that aren't game commands: *smiles*, *nods*, *scratches head*");
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
                // NPCs with key locations have automatic movement toward plan destinations
                if (this is IHasKeyLocations)
                {
                    sb.AppendLine("  [cmd:go <direction>] - AUTOMATIC (do NOT use - you will travel to plan locations automatically)");
                }
                else
                {
                    sb.AppendLine("  [cmd:go <direction>] - move to another room");
                }
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

        // Goal markup section
        sb.AppendLine("Goal Management:");
        sb.AppendLine("- You can set or update your current goal using [goal:...] markup");
        sb.AppendLine("- Set a goal: [goal:type] or [goal:type target] where target is a player name");
        sb.AppendLine("- Clear/complete a goal: [goal:done] or [goal:clear]");
        sb.AppendLine("- Example goals: [goal:sell_items], [goal:help_customer Mats], [goal:guard_area]");
        sb.AppendLine("- Goals persist across sessions - use them for long-term objectives");
        sb.AppendLine();

        // Plan markup section
        sb.AppendLine("Plan Management:");
        sb.AppendLine("- Create a plan for your current goal: [plan:step1|step2|step3]");
        sb.AppendLine("- Target a SPECIFIC goal: [plan:goalType:step1|step2|step3]");
        sb.AppendLine("- Steps are separated by | (pipe character)");
        sb.AppendLine("- Mark current step complete: [step:done] or [step:goalType:done]");
        sb.AppendLine("- Skip current step: [step:skip] or [step:goalType:skip]");
        sb.AppendLine("- Example: [plan:sell_items:greet customer|show wares|negotiate|complete sale]");
        sb.AppendLine("- Plans help you break down goals into actionable steps");
        sb.AppendLine();

        // Core rules (always included)
        sb.AppendLine("Rules:");
        sb.AppendLine("- NEVER break character");
        sb.AppendLine("- Respond with exactly ONE short action per event - one emote OR one sentence");
        sb.AppendLine("- Do NOT chain multiple actions together");
        sb.AppendLine("- Emotes are for expressions/gestures ONLY - *nods*, *smiles*, *looks around*");
        // NPCs with key locations have automatic movement, so don't tell them to use [cmd:go]
        if (this is IHasKeyLocations)
        {
            sb.AppendLine("- NEVER emote interactions: use [cmd:give/get/attack] instead (movement is automatic)");
        }
        else
        {
            sb.AppendLine("- NEVER emote movement or interactions: use [cmd:go/give/get/attack] instead");
        }
        if (!string.IsNullOrWhiteSpace(NpcExtraRules))
        {
            sb.AppendLine($"- {NpcExtraRules}");
        }

        return sb.ToString().TrimEnd();
    }

    #endregion
}
