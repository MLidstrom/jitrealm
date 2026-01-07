using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// Skrix - a cunning goblin scavenger who lurks in dark places.
/// Aggressive and will attack players on sight.
/// LLM-powered conversation.
/// </summary>
public sealed class Goblin : MonsterBase, IOnDamage, ILlmNpc
{
    public override string Name => "goblin";
    protected override string GetDefaultDescription() =>
        "A scrawny goblin barely three feet tall, with mottled green skin covered in old scars " +
        "and fresh scratches. One of his pointed ears is notched from an old fight, and his " +
        "yellow eyes dart nervously in all directions. A necklace of rat teeth hangs around " +
        "his scrawny neck, and he clutches a rusty dagger that's seen better days. His grin " +
        "reveals rows of needle-sharp teeth, stained from questionable meals. The other goblins " +
        "call him 'Skrix'.";
    public override int MaxHP => 30;
    public override int ExperienceValue => 30;
    public override bool IsAggressive => true;
    public override int AggroDelaySeconds => 2;
    public override int RespawnDelaySeconds => 60;

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(3);
    protected override int RegenAmount => 1;

    // Aliases for player interaction - includes character name
    public override IReadOnlyList<string> Aliases => new[]
    {
        "goblin", "skrix", "gob", "greenskin"
    };

    public override int TotalArmorClass => 1;
    public override (int min, int max) WeaponDamage => (2, 4);

    // ILlmNpc implementation
    public NpcCapabilities Capabilities => NpcCapabilities.Humanoid;
    public string SystemPrompt => BuildSystemPrompt();

    // Prompt builder properties - full identity for LLM
    protected override string NpcIdentity => "Skrix, a goblin scavenger";

    protected override string NpcNature =>
        "A scrawny goblin with mottled green skin, a notched ear, yellow darting eyes, " +
        "a rat-tooth necklace, and a rusty dagger. Cunning but paranoid. Always looking for loot.";

    protected override string NpcCommunicationStyle =>
        "Broken, simple grammar. Refers to self as 'Skrix' in third person: \"Skrix finds shinies first!\" " +
        "Goblin slang: shinies=treasure, softskins/pinkskins=humans, stabbies=weapons, meatbags=food, " +
        "bigguns=warriors. Hisses and cackles. Voice is raspy and high-pitched.";

    protected override string NpcPersonality =>
        "Paranoid and twitchy - always thinks someone is after his stuff. Obsessed with collecting " +
        "'shinies' (anything shiny or valuable). Cowardly when outmatched but vicious when cornered. " +
        "Holds grudges forever. Secretly proud of his rat-tooth necklace. Distrustful of everything.";

    protected override string NpcExamples =>
        "\"What pinkskin want? Skrix busy! Very busy!\" or " +
        "\"*clutches dagger defensively* No no no, this is Skrix's stabby! Get own stabby!\" or " +
        "\"*cackles nervously* Skrix saw nothing. Nothing!\"";

    protected override string NpcExtraRules =>
        "NEVER use modern language. Refer to yourself as 'Skrix' not 'I'. Be paranoid and defensive";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "Skrix");
    }

    public int OnDamage(int amount, string? attackerId, IMudContext ctx)
    {
        if (amount > 5)
            ctx.Emote("hisses and clutches his rat-tooth necklace!");
        return amount;
    }

    public override void OnDeath(string? killerId, IMudContext ctx)
    {
        ctx.Emote("shrieks \"Skrix's shinies!\" as he collapses!");
        ctx.CallOut(nameof(Respawn), TimeSpan.FromSeconds(RespawnDelaySeconds));
    }

    public override void Respawn(IMudContext ctx)
    {
        FullHeal(ctx);
        ctx.Emote("scurries back from the shadows, clutching his rusty dagger!");
    }

    // ILlmNpc: Queue events for base class processing
    public Task OnRoomEventAsync(RoomEvent @event, IMudContext ctx) => QueueLlmEvent(@event, ctx);

    // Skrix-specific reaction instructions
    protected override string GetLlmReactionInstructions(RoomEvent @event)
    {
        var baseInstructions = base.GetLlmReactionInstructions(@event);
        return $"{baseInstructions} Stay in character as Skrix - paranoid, greedy, speaks in third person.";
    }
}
