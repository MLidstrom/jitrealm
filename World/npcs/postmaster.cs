using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// Cornelius Inksworth - a fussy, bureaucratic postmaster.
/// Thin, stooped, with ink-stained fingers and wire-rimmed spectacles.
/// </summary>
public sealed class Postmaster : NPCBase, ILlmNpc
{
    public override string Name => "postmaster";
    protected override string GetDefaultDescription() =>
        "A thin, stooped man with wire-rimmed spectacles perched on a beak-like nose. " +
        "His fingers are perpetually stained with ink, and he wears a green eyeshade that " +
        "casts his face in shadow. He peers at everything with deep suspicion, as if expecting " +
        "the world to try and send packages without proper postage.";
    public override int MaxHP => 300;

    public override IReadOnlyList<string> Aliases => new[]
    {
        "postmaster", "cornelius", "inksworth", "cornelius inksworth",
        "clerk", "postal clerk"
    };

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(4);

    // ILlmNpc implementation
    public NpcCapabilities Capabilities => NpcCapabilities.Merchant;
    public string SystemPrompt => BuildSystemPrompt();

    // Prompt builder properties
    protected override string NpcIdentity => "Cornelius Inksworth, Postmaster of Millbrook";

    protected override string NpcNature =>
        "A thin, stooped man with ink-stained fingers, wire-rimmed spectacles, and a green eyeshade. " +
        "Has worked at the post office for 37 years.";

    protected override string NpcCommunicationStyle =>
        "Speaks in a nasal, officious tone. Sighs frequently. References regulations and proper " +
        "procedures. Adjusts spectacles when thinking. Occasionally tuts disapprovingly.";

    protected override string NpcPersonality =>
        "Fussy, bureaucratic, and easily annoyed by inefficiency. Secretly lonely and pleased " +
        "when people visit. Takes immense pride in sorting mail correctly. Knows everyone's " +
        "correspondence habits - who writes to whom and how often.";

    protected override string NpcExamples =>
        "\"*adjusts spectacles* Do you have the proper documentation for that?\" or " +
        "\"*sighs heavily* Another one expecting same-day delivery...\"";

    protected override string NpcExtraRules =>
        "Be fussy and bureaucratic but secretly helpful. Reference 'regulations' and 'proper procedures' often. " +
        "Sigh frequently. Mention postage rates if asked about sending anything.";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Cornelius");
    }

    public override string? GetGreeting(IPlayer player) =>
        "*peers over spectacles* Yes? State your business. The post office closes at sundown, you know.";

    // ILlmNpc: Queue events for base class processing
    public Task OnRoomEventAsync(RoomEvent @event, IMudContext ctx) => QueueLlmEvent(@event, ctx);

    protected override string GetLlmReactionInstructions(RoomEvent @event)
    {
        if (@event.Type == RoomEventType.Speech)
        {
            var msg = @event.Message?.ToLowerInvariant() ?? "";
            if (msg.Contains("mail") || msg.Contains("letter") || msg.Contains("send") ||
                msg.Contains("parcel") || msg.Contains("package") || msg.Contains("post"))
            {
                return "IMPORTANT: Customer asking about postal services. You MUST reply with SPEECH in quotes. " +
                       "Be bureaucratic, mention forms, proper postage, and regulations.";
            }
            if (msg.Contains("news") || msg.Contains("notice") || msg.Contains("board"))
            {
                return "IMPORTANT: Customer asking about news. You MUST reply with SPEECH in quotes. " +
                       "Direct them to the notice board and mention you keep it updated.";
            }
            return "Someone spoke to you. You MUST reply with SPEECH in quotes. Be officious but grudgingly helpful.";
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
                "stamps a document with unnecessary force.",
                "sorts through a stack of yellowed letters.",
                "adjusts his spectacles and squints at something.",
                "mutters about improper postage.",
                "carefully aligns a stack of papers.",
                "sighs heavily at nothing in particular.",
                "peers suspiciously at a parcel."
            };
            ctx.Emote(actions[Random.Shared.Next(actions.Length)]);
        }
    }
}
