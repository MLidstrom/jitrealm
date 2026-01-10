using System.Text.Json;
using System.Text.Json.Nodes;

namespace JitRealm.Mud.AI;

/// <summary>
/// Represents a plan to achieve an NPC goal, consisting of ordered steps.
/// Plans are stored in the goal's Params JSON field.
/// </summary>
public sealed class GoalPlan
{
    /// <summary>
    /// The ordered list of steps to complete the goal.
    /// </summary>
    public List<string> Steps { get; set; } = new();

    /// <summary>
    /// Index of the current step (0-based). -1 if no plan or plan complete.
    /// </summary>
    public int CurrentStep { get; set; } = 0;

    /// <summary>
    /// Indices of completed steps.
    /// </summary>
    public HashSet<int> CompletedSteps { get; set; } = new();

    /// <summary>
    /// True if all steps are completed.
    /// </summary>
    public bool IsComplete => Steps.Count > 0 && CompletedSteps.Count >= Steps.Count;

    /// <summary>
    /// True if there's a valid plan with steps.
    /// </summary>
    public bool HasPlan => Steps.Count > 0;

    /// <summary>
    /// Gets the current step text, or null if no current step.
    /// </summary>
    public string? CurrentStepText =>
        CurrentStep >= 0 && CurrentStep < Steps.Count ? Steps[CurrentStep] : null;

    /// <summary>
    /// Gets the progress as "step X/Y".
    /// </summary>
    public string Progress => HasPlan ? $"step {CurrentStep + 1}/{Steps.Count}" : "";

    /// <summary>
    /// Mark the current step as complete and advance to the next uncompleted step.
    /// Returns true if there are more steps, false if plan is now complete.
    /// </summary>
    public bool CompleteCurrentStep()
    {
        if (CurrentStep >= 0 && CurrentStep < Steps.Count)
        {
            CompletedSteps.Add(CurrentStep);
        }

        // Find next uncompleted step
        for (int i = CurrentStep + 1; i < Steps.Count; i++)
        {
            if (!CompletedSteps.Contains(i))
            {
                CurrentStep = i;
                return true;
            }
        }

        // Check for any skipped steps before current
        for (int i = 0; i < CurrentStep; i++)
        {
            if (!CompletedSteps.Contains(i))
            {
                CurrentStep = i;
                return true;
            }
        }

        // All steps complete
        CurrentStep = -1;
        return false;
    }

    /// <summary>
    /// Skip the current step without marking it complete.
    /// Returns true if there are more steps, false if at end.
    /// </summary>
    public bool SkipCurrentStep()
    {
        // Find next step (completed or not)
        if (CurrentStep + 1 < Steps.Count)
        {
            CurrentStep++;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parse a GoalPlan from a goal's Params JsonDocument.
    /// Returns an empty plan if no plan data exists.
    /// </summary>
    public static GoalPlan FromParams(JsonDocument? paramsDoc)
    {
        if (paramsDoc is null)
            return new GoalPlan();

        try
        {
            var root = paramsDoc.RootElement;
            if (!root.TryGetProperty("plan", out var planElement))
                return new GoalPlan();

            var plan = new GoalPlan();

            if (planElement.TryGetProperty("steps", out var stepsElement) &&
                stepsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var step in stepsElement.EnumerateArray())
                {
                    if (step.ValueKind == JsonValueKind.String)
                        plan.Steps.Add(step.GetString()!);
                }
            }

            if (planElement.TryGetProperty("currentStep", out var currentElement) &&
                currentElement.ValueKind == JsonValueKind.Number)
            {
                plan.CurrentStep = currentElement.GetInt32();
            }

            if (planElement.TryGetProperty("completedSteps", out var completedElement) &&
                completedElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var idx in completedElement.EnumerateArray())
                {
                    if (idx.ValueKind == JsonValueKind.Number)
                        plan.CompletedSteps.Add(idx.GetInt32());
                }
            }

            return plan;
        }
        catch
        {
            return new GoalPlan();
        }
    }

    /// <summary>
    /// Create a new GoalPlan from a pipe-separated list of steps.
    /// Example: "find customer|negotiate price|complete sale"
    /// </summary>
    public static GoalPlan FromSteps(string stepsList)
    {
        var plan = new GoalPlan();
        var steps = stepsList.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        plan.Steps.AddRange(steps);
        return plan;
    }

    /// <summary>
    /// Convert this plan to a JsonDocument for storage in goal Params.
    /// Merges with existing params if provided.
    /// </summary>
    public JsonDocument ToParams(JsonDocument? existingParams = null)
    {
        var obj = new JsonObject();

        // Copy existing params (except plan which we'll replace)
        if (existingParams is not null)
        {
            foreach (var prop in existingParams.RootElement.EnumerateObject())
            {
                if (prop.Name != "plan")
                {
                    obj[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
                }
            }
        }

        // Add plan data
        var planObj = new JsonObject
        {
            ["steps"] = new JsonArray(Steps.Select(s => JsonValue.Create(s)).ToArray()),
            ["currentStep"] = CurrentStep,
            ["completedSteps"] = new JsonArray(CompletedSteps.Select(i => JsonValue.Create(i)).ToArray())
        };
        obj["plan"] = planObj;

        return JsonDocument.Parse(obj.ToJsonString());
    }

    /// <summary>
    /// Get a summary string for display in LLM context.
    /// Example: "step 2/3: negotiate price"
    /// </summary>
    public string GetContextSummary()
    {
        if (!HasPlan)
            return "";

        if (IsComplete)
            return "plan complete";

        var step = CurrentStepText;
        return step is not null ? $"{Progress}: \"{step}\"" : Progress;
    }
}
