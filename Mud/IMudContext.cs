using JitRealm.Mud.AI;
using JitRealm.Mud.Security;

namespace JitRealm.Mud;

/// <summary>
/// Driver-provided context passed into lifecycle hooks and command handlers.
/// This is the primary API surface for world code (lpMUD driver boundary).
/// </summary>
public interface IMudContext
{
    /// <summary>
    /// Sandboxed world access providing read-only queries.
    /// Does not expose ObjectManager, SessionManager, or raw schedulers.
    /// </summary>
    ISandboxedWorldAccess World { get; }

    IStateStore State { get; }
    IClock Clock { get; }

    /// <summary>
    /// The ID of the object this context belongs to.
    /// </summary>
    string? CurrentObjectId { get; }

    /// <summary>
    /// Send a private message to a specific target.
    /// </summary>
    void Tell(string targetId, string message);

    /// <summary>
    /// Broadcast a message to everyone in the current room.
    /// </summary>
    void Say(string message);

    /// <summary>
    /// Broadcast an emote/action to everyone in the current room.
    /// </summary>
    void Emote(string action);

    /// <summary>
    /// Schedule a delayed method call on the current object.
    /// </summary>
    /// <param name="methodName">Name of the method to call (must accept IMudContext)</param>
    /// <param name="delay">Time to wait before calling</param>
    /// <param name="args">Optional arguments to pass</param>
    /// <returns>Callout ID for cancellation</returns>
    long CallOut(string methodName, TimeSpan delay, params object?[] args);

    /// <summary>
    /// Schedule a repeating method call on the current object.
    /// </summary>
    /// <param name="methodName">Name of the method to call</param>
    /// <param name="interval">Time between calls</param>
    /// <param name="args">Optional arguments to pass</param>
    /// <returns>Callout ID for cancellation</returns>
    long Every(string methodName, TimeSpan interval, params object?[] args);

    /// <summary>
    /// Cancel a scheduled callout.
    /// </summary>
    /// <param name="calloutId">ID returned by CallOut or Every</param>
    /// <returns>True if cancelled, false if not found</returns>
    bool CancelCallOut(long calloutId);

    // Living object methods (Phase 8)

    /// <summary>
    /// Deal damage to a living object.
    /// </summary>
    /// <param name="targetId">ID of the target living object</param>
    /// <param name="amount">Amount of damage to deal</param>
    /// <returns>True if damage was dealt, false if target not found or not ILiving</returns>
    bool DealDamage(string targetId, int amount);

    /// <summary>
    /// Heal a living object.
    /// </summary>
    /// <param name="targetId">ID of the target living object</param>
    /// <param name="amount">Amount to heal</param>
    /// <returns>True if healed, false if target not found or not ILiving</returns>
    bool HealTarget(string targetId, int amount);

    // Item and inventory methods (Phase 10)

    /// <summary>
    /// Move an object to a new container (room, inventory, or container item).
    /// </summary>
    /// <param name="objectId">ID of the object to move</param>
    /// <param name="destinationId">ID of the destination container</param>
    /// <returns>True if moved, false if object not found</returns>
    bool Move(string objectId, string destinationId);

    /// <summary>
    /// Get the total weight of items in a container.
    /// </summary>
    /// <param name="containerId">ID of the container (room, player, or container item)</param>
    /// <returns>Total weight of all IItem objects in the container</returns>
    int GetContainerWeight(string containerId);

    /// <summary>
    /// Get the items in the current object's inventory (when current object is a container).
    /// </summary>
    /// <returns>List of object IDs in the current object's inventory</returns>
    IReadOnlyCollection<string> GetInventory();

    /// <summary>
    /// Find an item by name in a container. Returns the first match.
    /// </summary>
    /// <param name="name">Item name to search for (case-insensitive partial match)</param>
    /// <param name="containerId">ID of the container to search</param>
    /// <returns>Item ID if found, null otherwise</returns>
    string? FindItem(string name, string containerId);

    // LLM methods (for AI-powered NPCs)

    /// <summary>
    /// Whether LLM service is enabled and available.
    /// </summary>
    bool IsLlmEnabled { get; }

    /// <summary>
    /// Generate an LLM completion for NPC dialogue or decision-making.
    /// </summary>
    /// <param name="systemPrompt">The system prompt defining NPC behavior.</param>
    /// <param name="userMessage">The player message or situation to respond to.</param>
    /// <returns>The LLM response, or null if LLM is disabled/unavailable.</returns>
    Task<string?> LlmCompleteAsync(string systemPrompt, string userMessage);

    /// <summary>
    /// Build an NpcContext with full environmental awareness for the current object.
    /// Includes room description, other entities, items, and recent events.
    /// </summary>
    /// <param name="npc">The NPC living object to build context for.</param>
    /// <param name="focalPlayerName">Optional stable player account/name the NPC is interacting with (for memory retrieval).</param>
    /// <param name="memoryQueryText">Optional text used to semantically recall relevant memories (pgvector + embeddings).</param>
    /// <returns>NpcContext with complete environmental information.</returns>
    Task<NpcContext> BuildNpcContextAsync(ILiving npc, string? focalPlayerName = null, string? memoryQueryText = null);

    /// <summary>
    /// Record an event that NPCs in the room can observe.
    /// Events are stored temporarily for LLM context building.
    /// </summary>
    /// <param name="eventDescription">Description of what happened.</param>
    void RecordEvent(string eventDescription);

    // NPC command execution

    /// <summary>
    /// Execute a player-like command on behalf of the current object.
    /// Allows NPCs to issue commands like "say Hello", "emote looks around", "go north".
    /// </summary>
    /// <param name="command">The command string to execute.</param>
    /// <returns>True if the command was executed, false if unrecognized or failed.</returns>
    Task<bool> ExecuteCommandAsync(string command);

    /// <summary>
    /// Parse and execute an LLM response that may contain mixed emotes and speech.
    /// Emotes wrapped in *asterisks* are executed as emotes, other text as speech.
    /// Example: "*hisss* Who you? *snarl*" becomes: emote hisses, say "Who you?", emote snarls
    /// </summary>
    /// <param name="response">The LLM response to parse and execute.</param>
    /// <param name="canSpeak">Whether the NPC can speak.</param>
    /// <param name="canEmote">Whether the NPC can emote.</param>
    /// <param name="interactorId">Optional ID of who the NPC is responding to (for "player" resolution).</param>
    Task ExecuteLlmResponseAsync(string response, bool canSpeak, bool canEmote, string? interactorId = null);

    // Coin methods (for shops and commerce)

    /// <summary>
    /// Get total coin value in copper for a container.
    /// </summary>
    /// <param name="containerId">ID of the container to check.</param>
    /// <returns>Total value in copper (1 GC = 10000 CC, 1 SC = 100 CC).</returns>
    int GetCopperValue(string containerId);

    /// <summary>
    /// Add coins to a container, merging with existing piles.
    /// </summary>
    /// <param name="containerId">ID of the container to add coins to.</param>
    /// <param name="copperAmount">Amount in copper to add (will be broken into optimal denominations).</param>
    Task AddCoinsAsync(string containerId, int copperAmount);

    /// <summary>
    /// Deduct coins from a container, with proper change handling.
    /// </summary>
    /// <param name="containerId">ID of the container to deduct from.</param>
    /// <param name="copperAmount">Amount in copper to deduct.</param>
    /// <returns>True if successful, false if insufficient funds.</returns>
    Task<bool> DeductCoinsAsync(string containerId, int copperAmount);

    // Goal methods (for NPC goals)

    /// <summary>
    /// Set a goal for the current NPC. Goals are stackable with priority based on importance.
    /// Lower importance = higher priority. Use GoalImportance constants.
    /// </summary>
    /// <param name="goalType">Type of goal (e.g., "help_customer", "patrol").</param>
    /// <param name="targetPlayer">Optional player the goal relates to.</param>
    /// <param name="status">Goal status (default "active").</param>
    /// <param name="importance">Priority level (1=highest/survival, 50=default, 100=background).</param>
    /// <returns>True if goal was set, false if memory system unavailable.</returns>
    Task<bool> SetGoalAsync(string goalType, string? targetPlayer = null, string status = "active", int importance = 50);

    /// <summary>
    /// Clear a specific goal type for the current NPC.
    /// </summary>
    /// <param name="goalType">Type of goal to clear.</param>
    /// <returns>True if cleared, false if memory system unavailable.</returns>
    Task<bool> ClearGoalAsync(string goalType);

    /// <summary>
    /// Clear all goals for the current NPC (except survival goal by default).
    /// </summary>
    /// <param name="preserveSurvival">Deprecated: survive is a drive (not a goal). This flag has no effect.</param>
    /// <returns>True if cleared, false if memory system unavailable.</returns>
    Task<bool> ClearAllGoalsAsync(bool preserveSurvival = true);

    /// <summary>
    /// Get the highest priority (lowest importance) goal for the current NPC.
    /// </summary>
    /// <returns>The highest priority goal, or null if none set.</returns>
    Task<NpcGoal?> GetGoalAsync();

    /// <summary>
    /// Get all goals for the current NPC, ordered by importance (highest priority first).
    /// </summary>
    /// <returns>List of goals ordered by importance.</returns>
    Task<IReadOnlyList<NpcGoal>> GetAllGoalsAsync();

    /// <summary>
    /// Evaluate a goal step using deterministic evaluators.
    /// Returns null if no evaluator applies, or the evaluation result.
    /// </summary>
    /// <param name="goal">The goal being pursued.</param>
    /// <param name="stepText">The current step text.</param>
    /// <returns>Evaluation result, or null if no evaluator applies.</returns>
    StepEvaluation? EvaluateGoalStep(NpcGoal goal, string stepText);

    /// <summary>
    /// Advance the current step of a goal (equivalent to [step:done]).
    /// </summary>
    /// <param name="goalType">The goal type.</param>
    /// <returns>True if step was advanced, false if no plan or already complete.</returns>
    Task<bool> AdvanceGoalStepAsync(string goalType);

    /// <summary>
    /// Skip the current step of a goal (equivalent to [step:skip]).
    /// </summary>
    /// <param name="goalType">The goal type.</param>
    /// <returns>True if step was skipped, false if no plan or already complete.</returns>
    Task<bool> SkipGoalStepAsync(string goalType);

    /// <summary>
    /// Set a plan on an existing goal using pipe-separated steps.
    /// </summary>
    /// <param name="goalType">The goal type to set the plan on.</param>
    /// <param name="planSteps">Pipe-separated plan steps (e.g., "step1|step2|step3").</param>
    /// <returns>True if plan was set, false if goal doesn't exist.</returns>
    Task<bool> SetGoalPlanAsync(string goalType, string planSteps);

    // Need/drive methods (for NPC needs)

    /// <summary>
    /// Set a need/drive for the current NPC. Level 1 is the top need (e.g., survive).
    /// </summary>
    Task<bool> SetNeedAsync(string needType, int level = 1, string status = "active");

    /// <summary>
    /// Clear a need/drive for the current NPC.
    /// </summary>
    Task<bool> ClearNeedAsync(string needType);

    /// <summary>
    /// Get all needs for the current NPC, ordered by level (lowest first).
    /// </summary>
    Task<IReadOnlyList<NpcNeed>> GetAllNeedsAsync();

    /// <summary>
    /// Log a debug message to the LLM debug log (if enabled).
    /// Use for debugging NPC behavior, wandering, etc.
    /// </summary>
    void LogDebug(string message);
}
