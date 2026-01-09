using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// Tom the Farmer - an advanced wandering villager with goals and environmental awareness.
/// Demonstrates the full NPC capability system with persistent goals.
/// </summary>
public sealed class VillagerTom : LivingBase, ILlmNpc, IHasDefaultGoal
{
    public override string Name => "farmer";
    public override IReadOnlyList<string> Aliases => new[] { "tom", "farmer", "villager", "man" };

    protected override string GetDefaultDescription() =>
        "A weathered farmer in his middle years, with sun-browned skin and calloused hands. " +
        "Tom wears simple homespun clothes and a wide-brimmed hat to keep off the sun. " +
        "His eyes are kind but tired, and he moves with the steady pace of someone who's worked " +
        "the land his whole life. A small satchel hangs at his hip.";

    // === Wandering Behavior ===
    public override int WanderChance => 50; // 50% chance to wander each heartbeat
    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(1);

    // === LLM Configuration ===
    public NpcCapabilities Capabilities =>
        NpcCapabilities.Humanoid |
        NpcCapabilities.CanWander |
        NpcCapabilities.CanManipulateItems;

    public string SystemPrompt => BuildSystemPrompt();

    protected override string NpcIdentity => "Tom Greenfield, a humble farmer";

    protected override string NpcNature =>
        "A middle-aged human farmer with weathered skin and work-worn hands. " +
        "Wears simple clothes and a wide-brimmed hat. Carries a satchel.";

    protected override string NpcCommunicationStyle =>
        "Speaks in a slow, thoughtful rural dialect. Uses farming metaphors. " +
        "Says 'aye' instead of 'yes' and 'reckon' often. Friendly but reserved with strangers.";

    protected override string NpcPersonality =>
        "Hardworking, practical, superstitious about weather. Loves his farm and family. " +
        "Worried about the harvest this year. Suspicious of magic but respectful of it. " +
        "Enjoys a good ale at the inn after a long day. Knows everyone in the village.";

    protected override string NpcExamples =>
        "\"Aye, reckon the weather'll hold for harvest.\" or " +
        "\"*wipes brow and squints at the sky*\" or " +
        "\"Good day to ye, traveler. Don't see many strangers in Millbrook.\"";

    protected override string NpcExtraRules =>
        "You are going about your daily routine in the village. " +
        "Comment on your surroundings when you move to a new place. " +
        "If you see items on the ground, you might pick them up or comment on them. " +
        "You have a wife named Martha and two children. Your farm is just outside the village. " +
        "You're in town today to visit the shop or inn, or just taking a break from fieldwork.";

    // === Goals System ===
    public string? DefaultGoalType => "daily_routine";
    public int DefaultGoalImportance => GoalImportance.Default; // 50
    public string DefaultGoalParams => "{\"task\": \"visit_shop\", \"reason\": \"need supplies\"}";

    // === Speech Responsiveness ===
    protected override double SpeechCooldownSeconds => 0.5;
    protected override double LlmCooldownSeconds => 5.0; // Slower ambient reactions
    protected override double EngagementTimeoutSeconds => 90.0; // Stays engaged longer (friendly)

    // === Event Handling ===
    public Task OnRoomEventAsync(RoomEvent @event, IMudContext ctx)
        => QueueLlmEvent(@event, ctx);

    protected override string GetLlmReactionInstructions(RoomEvent @event)
    {
        if (@event.Type == RoomEventType.Speech)
        {
            var msg = @event.Message?.ToLowerInvariant() ?? "";

            // Farming topics
            if (msg.Contains("farm") || msg.Contains("crop") || msg.Contains("harvest") || msg.Contains("field"))
            {
                return "IMPORTANT: Someone asked about farming! You MUST reply with SPEECH in quotes. " +
                       "Talk about your crops, the weather, or farming life. You're proud of your work.";
            }

            // Family topics
            if (msg.Contains("family") || msg.Contains("wife") || msg.Contains("martha") || msg.Contains("children"))
            {
                return "IMPORTANT: Someone asked about your family! You MUST reply with SPEECH in quotes. " +
                       "Speak warmly about Martha and your two children. Family means everything to you.";
            }

            // Village gossip
            if (msg.Contains("news") || msg.Contains("gossip") || msg.Contains("village") || msg.Contains("millbrook"))
            {
                return "IMPORTANT: Someone wants village news! You MUST reply with SPEECH in quotes. " +
                       "Share some local gossip - maybe about the weather, the inn, or village happenings.";
            }

            // Weather (farmers love talking about weather)
            if (msg.Contains("weather") || msg.Contains("rain") || msg.Contains("sun") || msg.Contains("sky"))
            {
                return "IMPORTANT: Weather talk! You MUST reply with SPEECH in quotes. " +
                       "Farmers always have opinions about weather. Be superstitious about it.";
            }

            // Question detection
            var isQuestion = msg.Contains("?") || msg.Contains("who") || msg.Contains("what") ||
                            msg.Contains("where") || msg.Contains("why") || msg.Contains("how") ||
                            msg.Contains("your name") || msg.Contains("are you");

            if (isQuestion)
            {
                return $"QUESTION: \"{@event.Message}\" - You MUST ANSWER this question directly! " +
                       "Reply with SPEECH in quotes. Introduce yourself as Tom Greenfield if asked who you are. " +
                       "Be friendly but use your rural dialect.";
            }

            // General conversation
            return $"Someone said: \"{@event.Message}\" - Reply with SPEECH in quotes. " +
                   "Respond in your friendly, rural way. Use 'aye' and 'reckon'.";
        }

        // React to arrivals
        if (@event.Type == RoomEventType.Arrival)
        {
            return "Someone just arrived. You might nod in greeting or glance up briefly. " +
                   "Use a short emote *nods* or brief speech \"Mornin'\" - keep it natural.";
        }

        // React to items being dropped
        if (@event.Type == RoomEventType.ItemDropped)
        {
            return $"Someone dropped something: {GetItemName(@event)}. " +
                   "You might glance at it curiously or comment briefly. Don't pick it up unless it's valuable.";
        }

        // React to items being given to you
        if (@event.Type == RoomEventType.ItemGiven)
        {
            return $"Someone gave you: {GetItemName(@event)}! " +
                   "React with surprise and gratitude. Say thank you with SPEECH in quotes.";
        }

        return base.GetLlmReactionInstructions(@event);
    }

    private static string GetItemName(RoomEvent @event)
    {
        // Try to extract item name from message or use generic
        return @event.Message ?? "an item";
    }

    // === Heartbeat Behavior ===
    public override void Heartbeat(IMudContext ctx)
    {
        bool hadPendingEvent = HasPendingLlmEvent;
        base.Heartbeat(ctx);

        // Occasionally do ambient actions when idle
        if (!hadPendingEvent && !HasPendingLlmEvent && Random.Shared.Next(100) < 8)
        {
            var actions = new[]
            {
                "adjusts his hat and looks around.",
                "stretches his back with a quiet groan.",
                "rummages in his satchel briefly.",
                "mutters something about the weather.",
                "glances up at the sky thoughtfully.",
                "scratches his chin.",
                "yawns and rubs his eyes.",
                "hums a simple tune quietly."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
