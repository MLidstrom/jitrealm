namespace JitRealm.Mud.Commands.Utility;

/// <summary>
/// Display the current game time.
/// </summary>
public class TimeCommand : CommandBase
{
    public override string Name => "time";
    public override IReadOnlyList<string> Aliases => new[] { "date" };
    public override string Usage => "time";
    public override string Description => "Show current time";
    public override string Category => "Utility";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var now = DateTimeOffset.Now;
        var player = context.GetPlayer();

        context.Output($"=== Time ===");
        context.Output($"Server time: {now:yyyy-MM-dd HH:mm:ss zzz}");

        if (player is not null)
        {
            var sessionTime = player.SessionTime;
            context.Output($"Session time: {FormatTimeSpan(sessionTime)}");

            // Get total playtime if available
            if (player is PlayerBase playerBase)
            {
                var totalTime = playerBase.TotalPlaytime;
                context.Output($"Total playtime: {FormatTimeSpan(totalTime)}");
            }
        }

        return Task.CompletedTask;
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
        {
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        }
        else if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        }
        else if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        }
        else
        {
            return $"{ts.Seconds}s";
        }
    }
}
