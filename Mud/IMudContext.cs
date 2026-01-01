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
    /// <returns>NpcContext with complete environmental information.</returns>
    NpcContext BuildNpcContext(ILiving npc);

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
    Task ExecuteLlmResponseAsync(string response, bool canSpeak, bool canEmote);
}
