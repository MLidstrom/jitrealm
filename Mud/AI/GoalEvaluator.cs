namespace JitRealm.Mud.AI;

/// <summary>
/// Result of evaluating a goal step.
/// </summary>
public enum StepEvaluationResult
{
    /// <summary>Step is not yet complete, continue as normal.</summary>
    InProgress,

    /// <summary>Step is complete, auto-advance to next step.</summary>
    Complete,

    /// <summary>Step is blocked/impossible, skip to next step.</summary>
    Blocked,

    /// <summary>Evaluator doesn't apply to this step type.</summary>
    NotApplicable
}

/// <summary>
/// Provides additional context about an evaluation result.
/// </summary>
public record StepEvaluation(
    StepEvaluationResult Result,
    string? Reason = null,
    string? SuggestedAction = null);

/// <summary>
/// Interface for goal step evaluators.
/// Evaluators check if a goal step is complete using game state (no LLM needed).
/// </summary>
public interface IGoalEvaluator
{
    /// <summary>
    /// Goal types this evaluator applies to (e.g., "reach_room", "acquire_item").
    /// </summary>
    IReadOnlyList<string> ApplicableGoalTypes { get; }

    /// <summary>
    /// Step keywords this evaluator can check (e.g., "go to", "visit", "get", "pick up").
    /// </summary>
    IReadOnlyList<string> ApplicableStepKeywords { get; }

    /// <summary>
    /// Evaluate whether the current step is complete.
    /// </summary>
    /// <param name="npcId">The NPC being evaluated.</param>
    /// <param name="goal">The goal being pursued.</param>
    /// <param name="stepText">The current step text.</param>
    /// <param name="state">World state for checking conditions.</param>
    /// <returns>Evaluation result.</returns>
    StepEvaluation Evaluate(string npcId, NpcGoal goal, string stepText, WorldState state);
}

/// <summary>
/// Registry of goal evaluators.
/// Evaluators are checked in order; first applicable one wins.
/// </summary>
public class GoalEvaluatorRegistry
{
    private readonly List<IGoalEvaluator> _evaluators = new();

    /// <summary>
    /// Register an evaluator.
    /// </summary>
    public void Register(IGoalEvaluator evaluator)
    {
        _evaluators.Add(evaluator);
    }

    /// <summary>
    /// Try to evaluate the current step of a goal.
    /// Returns null if no evaluator applies.
    /// </summary>
    public StepEvaluation? TryEvaluate(string npcId, NpcGoal goal, string stepText, WorldState state)
    {
        foreach (var evaluator in _evaluators)
        {
            // Check if evaluator applies to this goal type
            var appliesToGoal = evaluator.ApplicableGoalTypes.Count == 0 ||
                evaluator.ApplicableGoalTypes.Any(gt =>
                    goal.GoalType.Contains(gt, StringComparison.OrdinalIgnoreCase));

            if (!appliesToGoal)
                continue;

            // Check if evaluator applies to this step (by keyword)
            var appliesToStep = evaluator.ApplicableStepKeywords.Count == 0 ||
                evaluator.ApplicableStepKeywords.Any(kw =>
                    stepText.Contains(kw, StringComparison.OrdinalIgnoreCase));

            if (!appliesToStep)
                continue;

            var result = evaluator.Evaluate(npcId, goal, stepText, state);
            if (result.Result != StepEvaluationResult.NotApplicable)
                return result;
        }

        return null;
    }

    /// <summary>
    /// Create a registry with default evaluators.
    /// </summary>
    public static GoalEvaluatorRegistry CreateDefault()
    {
        var registry = new GoalEvaluatorRegistry();
        registry.Register(new ReachRoomEvaluator());
        registry.Register(new AcquireItemEvaluator());
        return registry;
    }
}

/// <summary>
/// Evaluates "reach room" / "go to" / "visit" type steps.
/// Checks if NPC is currently in the target location.
/// Uses the pathing daemon to suggest next direction when available.
/// </summary>
public class ReachRoomEvaluator : IGoalEvaluator
{
    public IReadOnlyList<string> ApplicableGoalTypes => new[] { "reach", "travel", "visit", "go_to", "patrol" };

    public IReadOnlyList<string> ApplicableStepKeywords => new[]
    {
        "go to", "visit", "reach", "travel to", "head to", "walk to",
        "return to", "arrive at", "get to"
    };

    public StepEvaluation Evaluate(string npcId, NpcGoal goal, string stepText, WorldState state)
    {
        // Extract target location from step text
        var targetRoom = ExtractTargetRoom(stepText);
        if (string.IsNullOrEmpty(targetRoom))
            return new StepEvaluation(StepEvaluationResult.NotApplicable);

        // Get NPC's current location
        var currentRoomId = state.Containers.GetContainer(npcId);
        if (currentRoomId is null)
            return new StepEvaluation(StepEvaluationResult.NotApplicable, "NPC location unknown");

        // Check if NPC is in the target room (fuzzy match on room name)
        var room = state.Objects?.Get<IRoom>(currentRoomId);
        if (room is null)
            return new StepEvaluation(StepEvaluationResult.NotApplicable);

        var roomName = room.Name?.ToLowerInvariant() ?? "";
        var targetLower = targetRoom.ToLowerInvariant();

        // Check room name or room ID
        if (roomName.Contains(targetLower) ||
            currentRoomId.Contains(targetLower, StringComparison.OrdinalIgnoreCase))
        {
            return new StepEvaluation(StepEvaluationResult.Complete, $"Reached {room.Name}");
        }

        // Try to find the target room by name and suggest a direction via pathing daemon
        var suggestedAction = TryGetPathingSuggestion(state, currentRoomId, targetRoom);

        return new StepEvaluation(StepEvaluationResult.InProgress, SuggestedAction: suggestedAction);
    }

    /// <summary>
    /// Try to get a pathing suggestion using PATHING_D daemon.
    /// </summary>
    private static string? TryGetPathingSuggestion(WorldState state, string currentRoomId, string targetRoom)
    {
        // Get the pathing daemon
        var pathingD = state.Daemons.Get<IPathingDaemon>("PATHING_D");
        if (pathingD is null)
            return null;

        // Try to find the target room by name
        var targetRoomId = FindRoomByName(state, targetRoom);
        if (targetRoomId is null)
            return null;

        // Get the next direction
        var nextDir = pathingD.GetNextDirection(currentRoomId, targetRoomId);
        if (nextDir is null)
            return null;

        return $"[cmd:go {nextDir}]";
    }

    /// <summary>
    /// Find a room by name (fuzzy match).
    /// </summary>
    private static string? FindRoomByName(WorldState state, string roomName)
    {
        if (state.Objects is null)
            return null;

        var targetLower = roomName.ToLowerInvariant();

        // Search loaded rooms for a match
        foreach (var objId in state.Objects.ListInstanceIds())
        {
            var room = state.Objects.Get<IRoom>(objId);
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

    private static string? ExtractTargetRoom(string stepText)
    {
        // Common patterns: "go to shop", "visit the tavern", "reach blacksmith"
        var patterns = new[]
        {
            "go to ", "visit ", "reach ", "travel to ", "head to ",
            "walk to ", "return to ", "arrive at ", "get to "
        };

        var lower = stepText.ToLowerInvariant();
        foreach (var pattern in patterns)
        {
            var idx = lower.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var target = stepText.Substring(idx + pattern.Length).Trim();
                // Remove trailing punctuation or "the"
                target = target.TrimStart("the ".ToCharArray());
                return target.TrimEnd('.', ',', '!');
            }
        }

        // Fallback: if step is just a place name like "shop" or "tavern"
        if (!stepText.Contains(' '))
            return stepText;

        return null;
    }
}

/// <summary>
/// Evaluates "acquire item" / "get" / "pick up" type steps.
/// Checks if NPC has the target item in inventory.
/// </summary>
public class AcquireItemEvaluator : IGoalEvaluator
{
    public IReadOnlyList<string> ApplicableGoalTypes => new[]
    {
        "acquire", "get", "obtain", "buy", "collect", "gather", "fetch"
    };

    public IReadOnlyList<string> ApplicableStepKeywords => new[]
    {
        "get ", "pick up", "take ", "acquire ", "buy ", "purchase ",
        "obtain ", "collect ", "gather ", "fetch ", "grab "
    };

    public StepEvaluation Evaluate(string npcId, NpcGoal goal, string stepText, WorldState state)
    {
        // Extract target item from step text
        var targetItem = ExtractTargetItem(stepText);
        if (string.IsNullOrEmpty(targetItem))
            return new StepEvaluation(StepEvaluationResult.NotApplicable);

        // Check NPC's inventory for the item
        var inventory = state.Containers.GetContents(npcId);
        foreach (var itemId in inventory)
        {
            var item = state.Objects?.Get<IItem>(itemId);
            if (item is null)
                continue;

            var itemName = item.Name?.ToLowerInvariant() ?? "";
            var shortDesc = item.ShortDescription?.ToLowerInvariant() ?? "";
            var targetLower = targetItem.ToLowerInvariant();

            // Check item name, short description, or aliases
            if (itemName.Contains(targetLower) ||
                shortDesc.Contains(targetLower) ||
                item.Aliases.Any(a => a.Contains(targetLower, StringComparison.OrdinalIgnoreCase)))
            {
                return new StepEvaluation(StepEvaluationResult.Complete, $"Has {item.ShortDescription}");
            }
        }

        return new StepEvaluation(StepEvaluationResult.InProgress);
    }

    private static string? ExtractTargetItem(string stepText)
    {
        // Common patterns: "get sword", "pick up bucket", "buy supplies"
        var patterns = new[]
        {
            "get ", "pick up ", "take ", "acquire ", "buy ", "purchase ",
            "obtain ", "collect ", "gather ", "fetch ", "grab "
        };

        var lower = stepText.ToLowerInvariant();
        foreach (var pattern in patterns)
        {
            var idx = lower.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var target = stepText.Substring(idx + pattern.Length).Trim();
                // Remove articles
                if (target.StartsWith("a ", StringComparison.OrdinalIgnoreCase))
                    target = target.Substring(2);
                if (target.StartsWith("an ", StringComparison.OrdinalIgnoreCase))
                    target = target.Substring(3);
                if (target.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                    target = target.Substring(4);
                return target.TrimEnd('.', ',', '!');
            }
        }

        return null;
    }
}
