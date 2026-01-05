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
    /// Queue an LLM event for processing on the next heartbeat.
    /// Call this from OnRoomEventAsync in ILlmNpc implementations.
    /// </summary>
    /// <param name="event">The room event to queue.</param>
    /// <param name="ctx">The MUD context.</param>
    /// <returns>A completed task.</returns>
    protected Task QueueLlmEvent(RoomEvent @event, IMudContext ctx)
    {
        // Don't respond if dead
        if (!IsAlive) return Task.CompletedTask;

        // Don't respond if LLM is disabled
        if (!ctx.IsLlmEnabled) return Task.CompletedTask;

        // Don't react to own actions
        if (@event.ActorId == ctx.CurrentObjectId) return Task.CompletedTask;

        // Queue the event for processing on next heartbeat
        _pendingLlmEvent = @event;
        return Task.CompletedTask;
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
            return $"Someone spoke to you. You MUST reply with speech in quotes (e.g. \"Your reply here\"). Do NOT use an emote. Give ONE short spoken response.";
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
        if (this is not ILlmNpc llmNpc) return;
        if (_pendingLlmEvent is null || !IsAlive || !ctx.IsLlmEnabled) return;

        var eventToProcess = _pendingLlmEvent;
        _pendingLlmEvent = null;

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

            // Parse and execute the response
            await ctx.ExecuteLlmResponseAsync(response,
                canSpeak: llmNpc.Capabilities.HasFlag(NpcCapabilities.CanSpeak),
                canEmote: llmNpc.Capabilities.HasFlag(NpcCapabilities.CanEmote));
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

        // Core rules (always included)
        sb.AppendLine("Rules:");
        sb.AppendLine("- NEVER break character");
        sb.AppendLine("- Respond with exactly ONE short action per event - one emote OR one sentence");
        sb.AppendLine("- Do NOT chain multiple actions together");
        if (!string.IsNullOrWhiteSpace(NpcExtraRules))
        {
            sb.AppendLine($"- {NpcExtraRules}");
        }

        return sb.ToString().TrimEnd();
    }

    #endregion
}
