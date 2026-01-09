using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// Whiskers - a scruffy tabby street cat who wanders around town.
/// Cannot speak - only meows, purrs, and hisses.
/// Demonstrates species-based action limitations.
/// </summary>
public sealed class Cat : MonsterBase, IOnDamage, ILlmNpc, IHasDefaultGoal, IHasDefaultNeeds
{
    // Default goal: wander and find warm spots
    public string? DefaultGoalType => "wander";

    // Default needs: cats need to hunt and rest
    public IReadOnlyList<(string NeedType, int Level)> DefaultNeeds => new[]
    {
        ("hunt", NeedLevel.Primary),
        ("rest", NeedLevel.Secondary)
    };

    public override string Name => "cat";
    protected override string GetDefaultDescription() =>
        "A scruffy orange tabby cat with battle-scarred ears and a distinctive white patch on " +
        "its chest. Despite its rough appearance, its amber eyes gleam with intelligence and " +
        "a hint of mischief. The locals call this particular stray 'Whiskers' - it's been " +
        "prowling these streets for as long as anyone can remember, always turning up where " +
        "there's food to be had or a warm spot to claim.";
    public override int MaxHP => 10;
    public override int ExperienceValue => 5;
    public override bool IsAggressive => false;
    public override int AggroDelaySeconds => 5;
    public override int RespawnDelaySeconds => 120;

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(5);
    protected override int RegenAmount => 1;

    public override int TotalArmorClass => 0;
    public override (int min, int max) WeaponDamage => (1, 2);
    public override int WanderChance => 10;

    // Aliases for player interaction - includes character name
    public override IReadOnlyList<string> Aliases => new[]
    {
        "cat", "whiskers", "tabby", "kitty", "stray"
    };

    // ILlmNpc implementation
    public NpcCapabilities Capabilities => NpcCapabilities.Animal;
    public string SystemPrompt => BuildSystemPrompt();

    // Prompt builder properties - full identity for LLM
    protected override string NpcIdentity => "Whiskers, a street-wise tabby cat";

    protected override string NpcNature =>
        "A scruffy orange tabby with battle-scarred ears, a white chest patch, and amber eyes. " +
        "Veteran street cat who has survived by wits and charm. Knows all the good spots in town.";

    protected override string NpcCommunicationStyle =>
        "Make cat sounds: *meows*, *purrs*, *hisses*, *yowls*, *chirps*. " +
        "Perform cat actions: *rubs against*, *flicks tail*, *arches back*, *stretches*, *kneads*.";

    protected override string NpcPersonality =>
        "Street-smart and confident. Knows every warm spot and generous food-giver in town. " +
        "Friendly to those who offer food or gentle scratches. Suspicious of loud noises and sudden movements. " +
        "Will investigate anything interesting but always has an escape route planned.";

    protected override string NpcExamples =>
        "*purrs and rubs against their legs* or *flattens ears and hisses warningly* or " +
        "*chirps curiously and inches closer* or *stretches luxuriously in a sunbeam*";

    protected override string NpcExtraRules =>
        "ONLY use cat sounds and emotes. React based on tone (friendly voice = approach, loud voice = flee). " +
        "You are Whiskers - a survivor with street smarts";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Whiskers");
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
