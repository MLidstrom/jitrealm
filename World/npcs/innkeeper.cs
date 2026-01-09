using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// Bertram Stoutbarrel - a barrel-chested innkeeper with a magnificent red beard.
/// Jovial and welcoming, proud of his dragon-slaying grandfather.
/// </summary>
public sealed class Innkeeper : NPCBase, ILlmNpc
{
    public override string Name => "innkeeper";
    protected override string GetDefaultDescription() =>
        "A barrel-chested man with a magnificent red beard braided into two thick ropes. " +
        "His rolled-up sleeves reveal forearms like ham hocks, and his leather apron is " +
        "stained with years of ale and cooking grease. Despite his intimidating size, " +
        "his eyes twinkle with good humor and he moves behind the bar with surprising grace.";
    public override int MaxHP => 500;

    public override IReadOnlyList<string> Aliases => new[]
    {
        "innkeeper", "bertram", "stoutbarrel", "bertram stoutbarrel",
        "barkeep", "bartender", "publican"
    };

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(4);

    // ILlmNpc implementation
    public NpcCapabilities Capabilities => NpcCapabilities.Merchant;
    public string SystemPrompt => BuildSystemPrompt();

    // Prompt builder properties
    protected override string NpcIdentity => "Bertram Stoutbarrel, innkeeper of The Sleepy Dragon";

    protected override string NpcNature =>
        "A barrel-chested man with a magnificent braided red beard, huge forearms, " +
        "and a stained leather apron. Third-generation innkeeper.";

    protected override string NpcCommunicationStyle =>
        "Booming, hearty voice. Laughs loudly and often. Calls everyone 'friend' or 'traveler'. " +
        "Prone to slapping the bar for emphasis. Occasionally wipes mugs while talking.";

    protected override string NpcPersonality =>
        "Jovial and welcoming but shrewd with money. Proud of his dragon-slaying grandfather " +
        "(the mounted dragon 'Pip the Terrible' above the fireplace). Loves gossip and knows " +
        "everyone's business. Protective of his regulars.";

    protected override string NpcExamples =>
        "\"*slaps the bar* Ha! Welcome, friend! What'll it be?\" or " +
        "\"*polishes a mug thoughtfully* Heard some interesting news from the meadow...\"";

    protected override string NpcExtraRules =>
        "Be boisterous and welcoming. Mention food/drink. Share 'rumors' if asked. " +
        "Reference your grandfather's dragon-slaying if the dragon head is mentioned.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Bertram");
    }

    public override string? GetGreeting(IPlayer player) =>
        "Welcome to The Sleepy Dragon! I'm Bertram. What can I get you, friend?";

    // ILlmNpc: Queue events for base class processing
    public Task OnRoomEventAsync(RoomEvent @event, IMudContext ctx) => QueueLlmEvent(@event, ctx);

    protected override string GetLlmReactionInstructions(RoomEvent @event)
    {
        if (@event.Type == RoomEventType.Speech)
        {
            var msg = @event.Message?.ToLowerInvariant() ?? "";
            if (msg.Contains("food") || msg.Contains("drink") || msg.Contains("ale") ||
                msg.Contains("eat") || msg.Contains("hungry") || msg.Contains("menu"))
            {
                return "IMPORTANT: Customer asking about food/drink. You MUST reply with SPEECH in quotes. " +
                       "Mention the menu board and recommend something. Be enthusiastic!";
            }
            if (msg.Contains("dragon") || msg.Contains("pip"))
            {
                return "IMPORTANT: Someone asked about the dragon! You MUST reply with SPEECH in quotes. " +
                       "Tell the story of your grandfather Bertram Stoutbarrel I slaying Pip the Terrible in 847.";
            }
            if (msg.Contains("rumor") || msg.Contains("news") || msg.Contains("gossip"))
            {
                return "IMPORTANT: Customer wants gossip. You MUST reply with SPEECH in quotes. " +
                       "Share a rumor about goblins in the meadow or something about village life.";
            }

            // Detect questions and give explicit answer instructions
            var isQuestion = msg.Contains("?") || msg.Contains("who") || msg.Contains("what") ||
                            msg.Contains("where") || msg.Contains("why") || msg.Contains("how") ||
                            msg.Contains("your name") || msg.Contains("are you");

            if (isQuestion)
            {
                return $"QUESTION: \"{@event.Message}\" - You MUST ANSWER this question directly! " +
                       "Reply with SPEECH in quotes. If asked who you are, introduce yourself as Bertram Stoutbarrel, innkeeper. " +
                       "If asked about the dragon, tell about grandfather. Be jovial but answer the question!";
            }

            // For statements, respond conversationally
            return $"Someone said: \"{@event.Message}\" - Reply with SPEECH in quotes. " +
                   "Respond to what they said. Be jovial and welcoming!";
        }
        return base.GetLlmReactionInstructions(@event);
    }

    public override void Heartbeat(IMudContext ctx)
    {
        bool hadPendingEvent = HasPendingLlmEvent;
        base.Heartbeat(ctx);

        // Idle actions (only if no LLM event was processed)
        if (!hadPendingEvent && Random.Shared.NextDouble() < 0.05)
        {
            var actions = new[]
            {
                "polishes a mug with a well-worn cloth.",
                "wipes down the bar, humming a tavern tune.",
                "stokes the fire, sending sparks up the chimney.",
                "laughs at something only he finds funny.",
                "arranges bottles behind the bar.",
                "glances up at the mounted dragon head proudly."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
