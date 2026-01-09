using System.Text.Json;
using JitRealm.Mud.AI;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// View or set NPC goals. Goals are stackable with priority based on importance.
/// </summary>
public class GoalCommand : WizardCommandBase
{
    public override string Name => "goal";
    public override IReadOnlyList<string> Aliases => Array.Empty<string>();
    public override string Usage => "goal <npc> [type [importance] [target]]";
    public override string Description => "View or set NPC goals (stackable with priority)";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.Output("Usage: goal <npc> [type [importance] [target]]");
            context.Output("  goal <npc>                        - show all NPC goals");
            context.Output("  goal <npc> <type>                 - add/update goal (default importance 50)");
            context.Output("  goal <npc> <type> <importance>    - add/update goal with importance");
            context.Output("  goal <npc> <type> <imp> <target>  - add/update goal with target");
            context.Output("  goal <npc> clear <type>           - clear specific goal");
            context.Output("  goal <npc> clearall               - clear all goals");
            context.Output("\nImportance levels (lower = higher priority):");
            context.Output("  5 = combat, 10 = urgent, 50 = default, 100 = background");
            return;
        }

        // Check if memory system is available
        var memorySystem = context.State.MemorySystem;
        if (memorySystem is null)
        {
            context.Output("Memory system is not available. Check that Postgres is configured.");
            return;
        }

        // Resolve NPC
        var npcId = context.ResolveObjectId(args[0]);
        if (npcId is null)
        {
            context.Output($"Could not find NPC: {args[0]}");
            return;
        }

        var npc = context.State.Objects?.Get<IMudObject>(npcId);
        if (npc is null)
        {
            context.Output($"Object not found: {npcId}");
            return;
        }

        if (npc is not ILiving)
        {
            context.Output($"Object is not a living entity: {npcId}");
            return;
        }

        // Show all goals if no additional args
        if (args.Length == 1)
        {
            var goals = await memorySystem.Goals.GetAllAsync(npcId);
            context.Output($"=== Goals for {npc.Name} ({npcId}) ===");

            if (goals.Count == 0)
            {
                context.Output("  No goals set");

                // Check if NPC has a default goal defined in code
                if (npc is IHasDefaultGoal hasDefault && hasDefault.DefaultGoalType is not null)
                {
                    context.Output($"\n  Default (not yet applied): {hasDefault.DefaultGoalType}");
                    context.Output($"    Importance: {hasDefault.DefaultGoalImportance}");
                    if (hasDefault.DefaultGoalTarget is not null)
                        context.Output($"    Target: {hasDefault.DefaultGoalTarget}");
                }
            }
            else
            {
                foreach (var goal in goals)
                {
                    context.Output($"\n  [{goal.Importance}] {goal.GoalType}");
                    context.Output($"      Status: {goal.Status}");
                    if (goal.TargetPlayer is not null)
                        context.Output($"      Target: {goal.TargetPlayer}");
                    context.Output($"      Updated: {goal.UpdatedAt:u}");
                }
            }
            return;
        }

        // Clear all goals
        if (args[1].Equals("clearall", StringComparison.OrdinalIgnoreCase))
        {
            await memorySystem.Goals.ClearAllAsync(npcId, preserveSurvival: false);
            context.Output($"Cleared all goals for {npc.Name}");
            return;
        }

        // Clear specific goal
        if (args[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 3)
            {
                context.Output("Usage: goal <npc> clear <type>");
                return;
            }
            var clearType = args[2].ToLowerInvariant();
            await memorySystem.Goals.ClearAsync(npcId, clearType);
            context.Output($"Cleared goal '{clearType}' for {npc.Name}");
            return;
        }

        // Set/add goal
        var goalType = args[1].ToLowerInvariant();
        if (goalType == "survive")
        {
            context.Output("'survive' is a drive (always-on) and cannot be set as a goal.");
            return;
        }
        var importance = GoalImportance.Default;
        string? targetPlayer = null;

        // Check if second arg is a number (importance)
        if (args.Length > 2)
        {
            if (int.TryParse(args[2], out var imp))
            {
                importance = imp;
                // Target is everything after importance
                if (args.Length > 3)
                    targetPlayer = string.Join(" ", args.Skip(3));
            }
            else
            {
                // Not a number, treat as target
                targetPlayer = string.Join(" ", args.Skip(2));
            }
        }

        var newGoal = new NpcGoal(
            NpcId: npcId,
            GoalType: goalType,
            TargetPlayer: targetPlayer,
            Params: JsonDocument.Parse("{}"),
            Status: "active",
            Importance: importance,
            UpdatedAt: DateTimeOffset.UtcNow);

        await memorySystem.Goals.UpsertAsync(newGoal);

        context.Output($"Set goal for {npc.Name}:");
        context.Output($"  Type: {goalType}");
        context.Output($"  Importance: {importance}");
        if (targetPlayer is not null)
            context.Output($"  Target: {targetPlayer}");
    }
}
