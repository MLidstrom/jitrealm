using JitRealm.Mud.AI;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard-only story/lore generator using the configured StoryModel (typically a larger creative model).
/// </summary>
public sealed class StoryCommand : WizardCommandBase
{
    public override string Name => "story";
    public override IReadOnlyList<string> Aliases => new[] { "lore", "write" };
    public override string Usage => "story <prompt>";
    public override string Description => "Generate fantasy story/lore text using the story-builder LLM model";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.Output("Usage: story <prompt>");
            context.Output("Aliases: lore, write");
            context.Output("\nExamples:");
            context.Output("  story Write a short quest hook for the general store.");
            context.Output("  story Describe an eerie forest clearing at midnight.");
            return;
        }

        var llm = context.State.LlmService;
        if (llm is null || !llm.IsEnabled)
        {
            context.Output("LLM service is not enabled.");
            return;
        }

        var prompt = string.Join(" ", args);

        // Keep this conservative: even if the underlying model is uncensored,
        // we ask for game-appropriate output by default.
        var systemPrompt =
            "You are a fantasy worldbuilder and writer for a text-based MUD.\n" +
            "Write vivid, usable game content (quests, lore, NPC backstory, room descriptions).\n" +
            "Keep it PG-13, avoid explicit sexual content, and keep violence non-graphic.\n" +
            "Prefer concrete details and hooks a wizard can paste into the game.\n" +
            "Output plain text only (no markdown headings).";

        context.Output("[Story] Thinking...");
        var result = await llm.CompleteAsync(systemPrompt, prompt, LlmProfile.Story);

        if (string.IsNullOrWhiteSpace(result))
        {
            context.Output("[Story] No response (LLM request failed or timed out).");
            return;
        }

        context.Output("=== Story Output ===");
        context.Output(result.Trim());
    }
}


