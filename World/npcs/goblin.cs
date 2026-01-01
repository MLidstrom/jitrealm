using System;
using System.Threading.Tasks;
using JitRealm.Mud;
using JitRealm.Mud.AI;

/// <summary>
/// A goblin monster that lurks in dark places.
/// Aggressive and will attack players on sight.
/// LLM-powered conversation.
/// </summary>
public sealed class Goblin : MonsterBase, IOnDamage, ILlmNpc
{
    public override string Name => "a goblin";
    public override string Description =>
        "A small, green-skinned creature with pointed ears, yellow eyes, and sharp teeth. It watches you with cunning hostility.";
    public override int MaxHP => 30;
    public override int ExperienceValue => 30;
    public override bool IsAggressive => true;
    public override int AggroDelaySeconds => 2;
    public override int RespawnDelaySeconds => 60;

    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(3);
    protected override int RegenAmount => 1;

    public override int TotalArmorClass => 1;
    public override (int min, int max) WeaponDamage => (2, 4);

    // ILlmNpc implementation
    public NpcCapabilities Capabilities => NpcCapabilities.Humanoid;
    public string SystemPrompt => BuildSystemPrompt();

    // Prompt builder properties
    protected override string NpcNature =>
        "Small, green-skinned with pointed ears, yellow eyes, sharp teeth. Cunning but not intelligent. Aggressive.";

    protected override string NpcCommunicationStyle =>
        "Broken, simple grammar. Drop articles. Third person sometimes: \"Goblin wants shinies!\" Goblin slang: shinies=treasure, softskins/pinkskins=humans, stabbies=weapons, meatbags=food, bigguns=warriors";

    protected override string NpcPersonality =>
        "Hostile, suspicious. Interested in shinies. Threatens violence. Remembers grudges. Greedy, cowardly when outmatched, cruel.";

    protected override string NpcExamples =>
        "\"What you want, pinkskin? Goblin busy!\" or \"*hisses* You no take goblin's shinies!\"";

    protected override string NpcExtraRules =>
        "NEVER use modern language. NEVER be helpful unless deceiving";

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);
        ctx.State.Set("name", "goblin");
    }

    public int OnDamage(int amount, string? attackerId, IMudContext ctx)
    {
        if (amount > 5)
            ctx.Emote("snarls angrily!");
        return amount;
    }

    public override void OnDeath(string? killerId, IMudContext ctx)
    {
        ctx.Emote("shrieks as it falls!");
        ctx.CallOut(nameof(Respawn), TimeSpan.FromSeconds(RespawnDelaySeconds));
    }

    public override void Respawn(IMudContext ctx)
    {
        FullHeal(ctx);
        ctx.Emote("crawls back from the shadows!");
    }

    // ILlmNpc: Queue events for base class processing
    public Task OnRoomEventAsync(RoomEvent @event, IMudContext ctx) => QueueLlmEvent(@event, ctx);

    // Goblin-specific reaction instructions
    protected override string GetLlmReactionInstructions(RoomEvent @event)
    {
        var baseInstructions = base.GetLlmReactionInstructions(@event);
        return $"{baseInstructions} Stay in character as a hostile goblin.";
    }
}
