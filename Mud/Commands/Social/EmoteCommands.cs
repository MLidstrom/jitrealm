using JitRealm.Mud;

namespace JitRealm.Mud.Commands.Social;

/// <summary>
/// Base class for predefined emote commands.
/// </summary>
public abstract class PredefinedEmoteCommand : CommandBase
{
    public override string Category => "Social";

    /// <summary>
    /// The emote action (e.g., "bows gracefully").
    /// </summary>
    protected abstract string EmoteAction { get; }

    /// <summary>
    /// Optional targeted emote (e.g., "bows to {target}").
    /// </summary>
    protected virtual string? TargetedEmote => null;

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var roomId = context.GetPlayerLocation();
        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return Task.CompletedTask;
        }

        string action;
        if (args.Length > 0 && TargetedEmote is not null)
        {
            var targetName = JoinArgs(args);
            action = TargetedEmote.Replace("{target}", targetName);
            context.Output($"You {action}");
        }
        else
        {
            action = EmoteAction;
            context.Output($"You {Name}.");
        }

        context.State.Messages.Enqueue(new MudMessage(
            context.PlayerId,
            null,
            MessageType.Emote,
            action,
            roomId
        ));

        return Task.CompletedTask;
    }
}

/// <summary>
/// Bow gracefully.
/// </summary>
public class BowCommand : PredefinedEmoteCommand
{
    public override string Name => "bow";
    public override string Usage => "bow [target]";
    public override string Description => "Bow gracefully";
    protected override string EmoteAction => "bows gracefully.";
    protected override string TargetedEmote => "bows to {target}.";
}

/// <summary>
/// Wave cheerfully.
/// </summary>
public class WaveCommand : PredefinedEmoteCommand
{
    public override string Name => "wave";
    public override string Usage => "wave [target]";
    public override string Description => "Wave cheerfully";
    protected override string EmoteAction => "waves cheerfully.";
    protected override string TargetedEmote => "waves at {target}.";
}

/// <summary>
/// Laugh heartily.
/// </summary>
public class LaughCommand : PredefinedEmoteCommand
{
    public override string Name => "laugh";
    public override IReadOnlyList<string> Aliases => new[] { "lol" };
    public override string Usage => "laugh [target]";
    public override string Description => "Laugh heartily";
    protected override string EmoteAction => "laughs heartily.";
    protected override string TargetedEmote => "laughs at {target}.";
}

/// <summary>
/// Smile warmly.
/// </summary>
public class SmileCommand : PredefinedEmoteCommand
{
    public override string Name => "smile";
    public override IReadOnlyList<string> Aliases => new[] { "grin" };
    public override string Usage => "smile [target]";
    public override string Description => "Smile warmly";
    protected override string EmoteAction => "smiles warmly.";
    protected override string TargetedEmote => "smiles at {target}.";
}

/// <summary>
/// Nod in agreement.
/// </summary>
public class NodCommand : PredefinedEmoteCommand
{
    public override string Name => "nod";
    public override string Usage => "nod [target]";
    public override string Description => "Nod in agreement";
    protected override string EmoteAction => "nods.";
    protected override string TargetedEmote => "nods at {target}.";
}

/// <summary>
/// Shake head.
/// </summary>
public class ShakeCommand : PredefinedEmoteCommand
{
    public override string Name => "shake";
    public override string Usage => "shake";
    public override string Description => "Shake your head";
    protected override string EmoteAction => "shakes their head.";
}

/// <summary>
/// Shrug shoulders.
/// </summary>
public class ShrugCommand : PredefinedEmoteCommand
{
    public override string Name => "shrug";
    public override string Usage => "shrug";
    public override string Description => "Shrug your shoulders";
    protected override string EmoteAction => "shrugs.";
}

/// <summary>
/// Sigh deeply.
/// </summary>
public class SighCommand : PredefinedEmoteCommand
{
    public override string Name => "sigh";
    public override string Usage => "sigh";
    public override string Description => "Sigh deeply";
    protected override string EmoteAction => "sighs deeply.";
}

/// <summary>
/// Cheer enthusiastically.
/// </summary>
public class CheerCommand : PredefinedEmoteCommand
{
    public override string Name => "cheer";
    public override string Usage => "cheer [target]";
    public override string Description => "Cheer enthusiastically";
    protected override string EmoteAction => "cheers enthusiastically!";
    protected override string TargetedEmote => "cheers for {target}!";
}

/// <summary>
/// Think deeply.
/// </summary>
public class ThinkCommand : PredefinedEmoteCommand
{
    public override string Name => "think";
    public override IReadOnlyList<string> Aliases => new[] { "ponder" };
    public override string Usage => "think";
    public override string Description => "Think deeply";
    protected override string EmoteAction => "thinks deeply.";
}

/// <summary>
/// Cry sadly.
/// </summary>
public class CryCommand : PredefinedEmoteCommand
{
    public override string Name => "cry";
    public override IReadOnlyList<string> Aliases => new[] { "sob" };
    public override string Usage => "cry";
    public override string Description => "Cry sadly";
    protected override string EmoteAction => "cries.";
}

/// <summary>
/// Dance happily.
/// </summary>
public class DanceCommand : PredefinedEmoteCommand
{
    public override string Name => "dance";
    public override string Usage => "dance [target]";
    public override string Description => "Dance happily";
    protected override string EmoteAction => "dances happily.";
    protected override string TargetedEmote => "dances with {target}.";
}

/// <summary>
/// Yawn tiredly.
/// </summary>
public class YawnCommand : PredefinedEmoteCommand
{
    public override string Name => "yawn";
    public override string Usage => "yawn";
    public override string Description => "Yawn tiredly";
    protected override string EmoteAction => "yawns tiredly.";
}
