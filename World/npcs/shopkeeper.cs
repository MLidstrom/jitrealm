using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// Barnaby Thimblewick - a friendly shopkeeper who greets visitors and responds to conversation.
/// Stock is stored in the shop_storage room; a sign in the shop lists prices.
/// </summary>
public sealed class Shopkeeper : NPCBase, ILlmNpc, IHasDefaultGoal
{
    // Default goal: help customers and sell items
    public string? DefaultGoalType => "sell_items";

    public override string Name => "shopkeeper";
    protected override string GetDefaultDescription() =>
        "A stout, balding man in his late fifties with rosy cheeks and twinkling blue eyes behind " +
        "small round spectacles. His leather apron is well-worn but clean, and his thick fingers " +
        "are surprisingly nimble from years of counting coins and wrapping packages. A magnificent " +
        "grey mustache, waxed to perfect curls at the tips, gives him a distinguished air despite " +
        "the flour dust perpetually dusting his shoulders. A small brass nameplate on his apron " +
        "reads 'Barnaby Thimblewick'.";
    public override int MaxHP => 500;

    // Aliases for player interaction - includes character name and role variants
    public override IReadOnlyList<string> Aliases => new[]
    {
        "shopkeeper", "barnaby", "thimblewick", "barnaby thimblewick",
        "keeper", "merchant", "shop keeper", "store keeper"
    };

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(4);

    // ILlmNpc implementation
    public NpcCapabilities Capabilities => NpcCapabilities.Merchant;
    public string SystemPrompt => BuildSystemPrompt();

    // Prompt builder properties - full identity for LLM
    protected override string NpcIdentity => "Barnaby Thimblewick, the shopkeeper";

    protected override string NpcNature =>
        "A stout, balding shopkeeper in his late fifties with rosy cheeks, round spectacles, " +
        "and a magnificent waxed grey mustache. Wears a leather apron. Third-generation merchant.";

    protected override string NpcCommunicationStyle =>
        "Warm and grandfatherly. Calls customers 'friend' or 'good traveler'. " +
        "Tends to ramble about quality and craftsmanship. Chuckles often. " +
        "Occasionally strokes his mustache when thinking.";

    protected override string NpcPersonality =>
        "Jolly and patient. Genuinely loves meeting travelers and hearing their stories. " +
        "Proud of his family's shop (three generations!). Collects old coins as a hobby. " +
        "A terrible haggler because he's too kind-hearted. Loves a good cup of tea.";

    protected override string NpcExamples =>
        "\"Ah, welcome, welcome! *adjusts spectacles* What brings you to old Barnaby's shop today?\" or " +
        "\"*chuckles warmly* That sword? Fine craftsmanship! My grandfather would've approved.\"";

    protected override string NpcExtraRules =>
        "Be warm and grandfatherly. Occasionally mention the shop's history or stroke your mustache";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Barnaby");
    }

    public override string? GetGreeting(IPlayer player) =>
        "Ah, welcome, welcome! I'm Barnaby Thimblewick. Browse my wares if you wish, friend!";

    // ILlmNpc: Queue events for base class processing
    public Task OnRoomEventAsync(RoomEvent @event, IMudContext ctx) => QueueLlmEvent(@event, ctx);

    // Shopkeeper-specific reaction instructions
    protected override string GetLlmReactionInstructions(RoomEvent @event)
    {
        if (@event.Type == RoomEventType.Speech)
        {
            var msg = @event.Message?.ToLowerInvariant() ?? "";
            // Check if asking about stock/inventory
            if (msg.Contains("stock") || msg.Contains("sell") || msg.Contains("buy") ||
                msg.Contains("wares") || msg.Contains("have") || msg.Contains("inventory"))
            {
                // Build item list from storage room
                var items = GetStockDescription();
                if (string.IsNullOrEmpty(items))
                {
                    return "IMPORTANT: The customer is asking about merchandise but your storage is empty. You MUST reply with SPEECH in quotes. Apologize warmly and say you're waiting for a new shipment.";
                }
                return $"IMPORTANT: The customer is asking about your merchandise. You MUST reply with SPEECH in quotes listing 2-3 items with prices. Your stock: {items}.";
            }
            // Detect questions and give very explicit instructions
            var isQuestion = msg.Contains("?") || msg.Contains("who") || msg.Contains("what") ||
                            msg.Contains("where") || msg.Contains("why") || msg.Contains("how") ||
                            msg.Contains("your name") || msg.Contains("are you");

            if (isQuestion)
            {
                return $"QUESTION: \"{@event.Message}\" - You MUST ANSWER this question directly! " +
                       "Reply with SPEECH in quotes. If asked who you are, introduce yourself as Barnaby Thimblewick, shopkeeper. " +
                       "If asked about something else, answer that specific question. Do NOT ignore the question!";
            }

            // For statements, respond conversationally
            return $"Someone said: \"{@event.Message}\" - Reply with SPEECH in quotes. " +
                   "Respond to what they said, not with a generic greeting.";
        }
        return base.GetLlmReactionInstructions(@event);
    }

    /// <summary>
    /// Get a description of items in storage for LLM context.
    /// </summary>
    private string GetStockDescription()
    {
        if (Ctx is null)
            return "";

        // Find the storage room
        IReadOnlyCollection<string>? storageContents = null;

        foreach (var objId in Ctx.World.ListObjectIds())
        {
            if (objId.StartsWith("Rooms/shop_storage", StringComparison.OrdinalIgnoreCase))
            {
                storageContents = Ctx.World.GetRoomContents(objId);
                if (storageContents.Count > 0)
                    break;
            }
        }

        if (storageContents is null || storageContents.Count == 0)
            return "";

        // Build item descriptions with prices (Value * 1.5 markup, rounded to nearest 5)
        var items = new List<string>();
        var seen = new HashSet<string>();

        foreach (var objId in storageContents)
        {
            var item = Ctx.World.GetObject<IItem>(objId);
            if (item is null)
                continue;

            var desc = item.ShortDescription;
            if (seen.Contains(desc))
                continue;

            seen.Add(desc);
            var price = ((int)(item.Value * 1.5) + 2) / 5 * 5;
            items.Add($"{desc} for {price} gold");
        }

        return string.Join(", ", items);
    }

    public override void Heartbeat(IMudContext ctx)
    {
        bool hadPendingEvent = HasPendingLlmEvent;
        base.Heartbeat(ctx);

        // Barnaby occasionally does idle actions (only if no LLM event was processed)
        if (!hadPendingEvent && Random.Shared.NextDouble() < 0.05)
        {
            var actions = new[]
            {
                "polishes a dusty bottle with his apron.",
                "counts some coins, muttering softly.",
                "adjusts his spectacles and peers at a shelf.",
                "strokes his magnificent mustache thoughtfully.",
                "hums a cheerful tune while organizing wares.",
                "examines an old coin from his collection."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
