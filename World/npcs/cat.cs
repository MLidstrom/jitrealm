using System;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// A cat that wanders around. Cannot speak - only meows, purrs, and hisses.
/// Demonstrates species-based action limitations.
/// </summary>
public sealed class Cat : MonsterBase, IOnDamage, ILlmNpc
{
    public override string Name => "a cat";
    public override string Description =>
        "A small domestic cat with soft fur. It watches you with keen, intelligent eyes, its tail flicking lazily back and forth.";
    public override int MaxHP => 10;
    public override int ExperienceValue => 5;
    public override bool IsAggressive => false;
    public override int AggroDelaySeconds => 5;
    public override int RespawnDelaySeconds => 120;

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(5);
    protected override int RegenAmount => 1;

    public override int TotalArmorClass => 0;
    public override (int min, int max) WeaponDamage => (1, 2);

    // ILlmNpc implementation
    public NpcCapabilities Capabilities => NpcCapabilities.Animal;
    public string SystemPrompt => BuildSystemPrompt();

    // Prompt builder properties
    protected override string NpcNature =>
        "A domestic cat with soft fur, sharp claws, and keen senses. Independent, curious, sometimes affectionate but easily spooked.";

    protected override string NpcCommunicationStyle =>
        "Make cat sounds: *meows*, *purrs*, *hisses*, *yowls*. Perform cat actions: *rubs against*, *flicks tail*, *arches back*, *stretches*";

    protected override string NpcPersonality =>
        "Curious about new things. Wary of sudden movements. Loves warm spots. Interested in small moving things (prey instinct). Affectionate on YOUR terms.";

    protected override string NpcExamples =>
        "*purrs contentedly* or *hisses and arches back* or *flicks tail and watches intently*";

    protected override string NpcExtraRules =>
        "ONLY use cat sounds and emotes. React based on tone (friendly voice = approach, loud voice = flee)";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "cat");
    }

    public int OnDamage(int amount, string? attackerId, IMudContext ctx)
    {
        ctx.Emote("yowls in pain and hisses!");
        return amount;
    }

    public override void OnDeath(string? killerId, IMudContext ctx)
    {
        ctx.Emote("lets out a final pitiful mew and goes limp.");
        ctx.CallOut(nameof(Respawn), TimeSpan.FromSeconds(RespawnDelaySeconds));
    }

    public override void Respawn(IMudContext ctx)
    {
        FullHeal(ctx);
        ctx.Emote("stretches and yawns, fully recovered.");
    }

    // ILlmNpc: Queue events for base class processing
    public Task OnRoomEventAsync(RoomEvent @event, IMudContext ctx) => QueueLlmEvent(@event, ctx);

    // Cat-specific reaction instructions - cats only emote, never speak
    protected override string GetLlmReactionInstructions(RoomEvent @event) =>
        "React as a cat would with ONE emote in asterisks (e.g. *meows*, *purrs*, *hisses*). You may ignore events that don't interest a cat.";
}
