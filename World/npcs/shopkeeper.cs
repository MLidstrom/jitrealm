using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// A friendly shopkeeper who greets visitors and responds to conversation.
/// Stock is stored in the shop_storage room; a sign in the shop lists prices.
/// </summary>
public sealed class Shopkeeper : NPCBase, ILlmNpc
{
    public override string Name => "the shopkeeper";
    protected override string GetDefaultDescription() =>
        "A warm, welcoming merchant with kind eyes and weathered hands. Years of trade have given him a shrewd but friendly demeanor.";
    public override int MaxHP => 500;

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(4);

    // ILlmNpc implementation
    public NpcCapabilities Capabilities => NpcCapabilities.Merchant;
    public string SystemPrompt => BuildSystemPrompt();

    // Prompt builder properties
    protected override string NpcNature =>
        "A warm, welcoming merchant who runs a general store. Middle-aged, experienced, wise about the world.";

    protected override string NpcCommunicationStyle =>
        "Friendly and professional. Occasionally mention wares or deals. Use phrases like \"traveler\", \"friend\", \"good day\". Helpful but shrewd";

    protected override string NpcPersonality =>
        "Welcoming to customers. Interested in news and gossip. Proud of shop and goods. Knowledgeable about local area. Fair but profit-minded.";

    protected override string NpcExamples =>
        "\"Ah, welcome friend! Looking for something special today?\" or \"*nods warmly* Good to see you again!\"";

    protected override string NpcExtraRules =>
        "Be friendly but not overly chatty. Keep responses brief and natural";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "shopkeeper");
    }

    public override string? GetGreeting(IPlayer player) =>
        "Welcome, traveler! Browse my wares if you wish.";

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
                    return "IMPORTANT: The customer is asking about merchandise but your storage is empty. Apologize and say you're waiting for a new shipment. Reply with speech.";
                }
                return $"IMPORTANT: The customer is asking about your merchandise. You MUST list some items and prices in your response. Your stock: {items}. Reply with speech listing 2-3 items with prices.";
            }
            return "Someone spoke to you. Reply with friendly speech in quotes. Be warm and helpful.";
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

        // Shopkeeper occasionally does idle actions (only if no LLM event was processed)
        if (!hadPendingEvent && Random.Shared.NextDouble() < 0.05)
        {
            var actions = new[]
            {
                "polishes a dusty bottle.",
                "counts some coins.",
                "arranges items on a shelf.",
                "hums a quiet tune."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
