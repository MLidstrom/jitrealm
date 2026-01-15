using JitRealm.Mud.Network;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to summon a player or NPC to the wizard's location.
/// </summary>
public class SummonCommand : WizardCommandBase
{
    public override string Name => "summon";
    public override string[] Aliases => Array.Empty<string>();
    public override string Usage => "summon <target>";
    public override string Description => "Bring a player or NPC to your location";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1))
            return;

        var targetRef = string.Join(" ", args);
        var wizardRoomId = context.GetPlayerLocation();

        if (wizardRoomId is null)
        {
            context.Output("You are not in a room.");
            return;
        }

        // Try to find target
        string? targetId = null;
        ISession? targetSession = null;
        bool isPlayer = false;

        // First, check if it's a player name
        targetSession = context.State.Sessions.GetByPlayerName(targetRef);
        if (targetSession?.PlayerId is not null)
        {
            targetId = targetSession.PlayerId;
            isPlayer = true;
        }

        // If not a player, try to find as NPC by ID or name
        if (targetId is null)
        {
            targetId = context.ResolveObjectId(targetRef);
        }

        // Search all instances for NPC by name/alias
        if (targetId is null)
        {
            targetId = FindLivingByNameOrAlias(context, targetRef.ToLowerInvariant());
        }

        if (targetId is null)
        {
            context.Output($"Cannot find target: {targetRef}");
            return;
        }

        // Get target object
        var living = context.State.Objects?.Get<ILiving>(targetId);
        if (living is null)
        {
            context.Output($"{targetRef} is not a living entity.");
            return;
        }

        // Get current location
        var oldRoomId = context.State.Containers.GetContainer(targetId);
        if (oldRoomId == wizardRoomId)
        {
            context.Output($"{living.Name} is already here.");
            return;
        }

        // Trigger OnLeave on old room
        if (oldRoomId is not null)
        {
            var oldRoom = context.State.Objects?.Get<IRoom>(oldRoomId);
            if (oldRoom is IOnLeave onLeave)
            {
                var oldCtx = context.CreateContext(oldRoomId);
                try
                {
                    onLeave.OnLeave(oldCtx, targetId);
                }
                catch
                {
                    // Ignore hook errors
                }
            }

            // Announce departure to old room
            var oldRoomSessions = context.State.Sessions.GetSessionsInRoom(
                oldRoomId,
                context.State.Containers.GetContainer
            );
            foreach (var session in oldRoomSessions)
            {
                if (session.PlayerId != targetId)
                {
                    await session.WriteLineAsync($"{living.Name} vanishes in a flash of light!");
                }
            }
        }

        // Move target to wizard's room
        context.State.Containers.Move(targetId, wizardRoomId);

        // Trigger OnEnter on new room
        var wizardRoom = context.State.Objects?.Get<IRoom>(wizardRoomId);
        if (wizardRoom is IOnEnter onEnter)
        {
            var newCtx = context.CreateContext(wizardRoomId);
            try
            {
                onEnter.OnEnter(newCtx, targetId);
            }
            catch
            {
                // Ignore hook errors
            }
        }

        // Announce arrival to wizard's room
        var wizardRoomSessions = context.State.Sessions.GetSessionsInRoom(
            wizardRoomId,
            context.State.Containers.GetContainer
        );
        foreach (var session in wizardRoomSessions)
        {
            if (session.PlayerId != targetId && session.PlayerId != context.PlayerId)
            {
                await session.WriteLineAsync($"{living.Name} appears in a flash of light!");
            }
        }

        // Notify the summoned player
        if (isPlayer && targetSession is not null)
        {
            await targetSession.WriteLineAsync($"You have been summoned by {context.Session.PlayerName}!");
            await targetSession.WriteLineAsync("");

            // Show them the room
            if (wizardRoom is not null)
            {
                await targetSession.WriteLineAsync($"=== {wizardRoom.Name} ===");
                await targetSession.WriteLineAsync(wizardRoom.Description);
            }
        }

        context.Output($"You summon {living.Name} to your location.");
    }

    private static string? FindLivingByNameOrAlias(CommandContext context, string name)
    {
        var instanceIds = context.State.Objects?.ListInstanceIds() ?? Array.Empty<string>();

        // First pass: exact matches
        foreach (var instanceId in instanceIds)
        {
            var obj = context.State.Objects?.Get<IMudObject>(instanceId);
            if (obj is null || obj is not ILiving living)
                continue;

            // Exact name match
            if (obj.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return instanceId;

            // Exact alias match
            foreach (var alias in living.Aliases)
            {
                if (alias.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return instanceId;
            }
        }

        // Second pass: partial matches
        foreach (var instanceId in instanceIds)
        {
            var obj = context.State.Objects?.Get<IMudObject>(instanceId);
            if (obj is null || obj is not ILiving living)
                continue;

            // Partial name match
            if (obj.Name.ToLowerInvariant().Contains(name))
                return instanceId;

            // Partial alias match
            foreach (var alias in living.Aliases)
            {
                if (alias.ToLowerInvariant().Contains(name))
                    return instanceId;
            }
        }

        return null;
    }
}
