using System;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// A friendly shopkeeper who greets visitors and responds to conversation.
/// Can be extended to support buy/sell commands in the future.
/// </summary>
public sealed class Shopkeeper : NPCBase, ILlmNpc
{
    public override string Name => "the shopkeeper";
    public override string Description =>
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
        var baseInstructions = base.GetLlmReactionInstructions(@event);
        return @event.Type == RoomEventType.Speech
            ? $"{baseInstructions} Be warm and friendly."
            : baseInstructions;
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
