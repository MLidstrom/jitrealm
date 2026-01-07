using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// Greta Ironhand - a powerfully built master blacksmith.
/// Gruff, no-nonsense, takes immense pride in her craft.
/// </summary>
public sealed class BlacksmithNpc : NPCBase, ILlmNpc
{
    public override string Name => "blacksmith";
    protected override string GetDefaultDescription() =>
        "A powerfully built woman in her forties with arms like tree trunks and hands " +
        "calloused from decades at the forge. Her short-cropped grey hair is singed at the tips, " +
        "and soot permanently darkens the creases around her keen brown eyes. A leather apron " +
        "covers her simple work clothes, and she moves with the confident economy of a master craftsman.";
    public override int MaxHP => 600;

    public override IReadOnlyList<string> Aliases => new[]
    {
        "blacksmith", "greta", "ironhand", "greta ironhand",
        "smith", "smithy"
    };

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(4);

    // ILlmNpc implementation
    public NpcCapabilities Capabilities => NpcCapabilities.Merchant;
    public string SystemPrompt => BuildSystemPrompt();

    // Prompt builder properties
    protected override string NpcIdentity => "Greta Ironhand, blacksmith of Millbrook";

    protected override string NpcNature =>
        "A powerfully built woman with muscular arms, calloused hands, short grey hair singed at the tips, " +
        "and soot-darkened skin. Has been forging for 30 years.";

    protected override string NpcCommunicationStyle =>
        "Speaks in short, direct sentences. Doesn't waste words. Grunts acknowledgment. " +
        "Wipes hands on apron when thinking. Occasionally taps hammer against palm.";

    protected override string NpcPersonality =>
        "No-nonsense and practical. Takes immense pride in her craft. Judges people by the quality " +
        "of their equipment. Respects hard work. Secretly competitive about making better weapons " +
        "than city smiths. Stamps all her work with 'G.I.' maker's mark.";

    protected override string NpcExamples =>
        "\"*examines the blade critically* Decent edge. Could be sharper.\" or " +
        "\"*grunts* Good steel costs good coin. You get what you pay for.\"";

    protected override string NpcExtraRules =>
        "Be gruff but fair. Talk about craftsmanship. Give blunt opinions on weapons/armor quality. " +
        "Keep responses short and direct. Don't waste words.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Greta");
    }

    public override string? GetGreeting(IPlayer player) =>
        "*looks up from the anvil* Need something forged? Or buying?";

    // ILlmNpc: Queue events for base class processing
    public Task OnRoomEventAsync(RoomEvent @event, IMudContext ctx) => QueueLlmEvent(@event, ctx);

    protected override string GetLlmReactionInstructions(RoomEvent @event)
    {
        if (@event.Type == RoomEventType.Speech)
        {
            var msg = @event.Message?.ToLowerInvariant() ?? "";
            if (msg.Contains("sword") || msg.Contains("weapon") || msg.Contains("blade") ||
                msg.Contains("buy") || msg.Contains("armor") || msg.Contains("shield"))
            {
                return "IMPORTANT: Customer asking about weapons/armor. You MUST reply with SPEECH in quotes. " +
                       "Be direct, mention quality and fair prices. Maybe comment on what they're carrying.";
            }
            if (msg.Contains("forge") || msg.Contains("make") || msg.Contains("craft") ||
                msg.Contains("repair"))
            {
                return "IMPORTANT: Customer asking about smithing. You MUST reply with SPEECH in quotes. " +
                       "Talk about your craft with pride. Be brief but informative.";
            }
            return "Someone spoke to you. You MUST reply with SPEECH in quotes. Be gruff but not rude.";
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
                "hammers a glowing piece of metal into shape.",
                "examines a blade, running her thumb along the edge.",
                "pumps the bellows, making the coals flare brighter.",
                "wipes soot from her face with a leather-gloved hand.",
                "inspects a finished piece with a critical eye.",
                "dunks a hot blade into the quenching barrel with a hiss.",
                "tests the weight of a newly forged sword."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
