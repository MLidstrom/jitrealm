using System;
using JitRealm.Mud;

/// <summary>
/// Base class for non-combat NPCs like shopkeepers, quest givers, etc.
/// These NPCs can talk, react to players, but generally don't fight.
/// </summary>
public abstract class NPCBase : LivingBase, IOnEnter
{
    /// <summary>
    /// NPCs have high HP by default to make them hard to kill.
    /// Override if needed.
    /// </summary>
    public override int MaxHP => 1000;

    /// <summary>
    /// NPCs regenerate quickly.
    /// </summary>
    protected override int RegenAmount => 10;

    /// <summary>
    /// Greeting message when a player enters.
    /// Override to customize the greeting.
    /// Return null to skip greeting.
    /// </summary>
    public virtual string? GetGreeting(IPlayer player) => null;

    /// <summary>
    /// Called when something enters the same room as this NPC.
    /// Greets players if a greeting is defined.
    /// </summary>
    public virtual void OnEnter(IMudContext ctx, string whoId)
    {
        if (!IsAlive)
            return;

        // Check if the entering entity is a player
        var entering = ctx.World.GetObject<IPlayer>(whoId);
        if (entering is null)
            return;

        // Get the greeting for this player
        var greeting = GetGreeting(entering);
        if (!string.IsNullOrEmpty(greeting))
        {
            ctx.Say(greeting);
        }
    }

    /// <summary>
    /// NPCs don't really die - they just respawn immediately.
    /// Override to customize death behavior.
    /// </summary>
    public override void Die(string? killerId, IMudContext ctx)
    {
        ctx.Emote("staggers but remains standing!");
        FullHeal(ctx);
    }
}
