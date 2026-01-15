namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to request a graceful server shutdown.
/// </summary>
public class ShutdownCommand : WizardCommandBase
{
    public override string Name => "shutdown";
    public override string[] Aliases => Array.Empty<string>();
    public override string Usage => "shutdown [delay_seconds] | shutdown cancel";
    public override string Description => "Request a graceful server shutdown";

    // Static state for pending shutdown
    private static CancellationTokenSource? _shutdownCts;
    private static DateTime? _shutdownTime;
    private static Task? _shutdownTask;

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        // Cancel command
        if (args.Length > 0 && args[0].Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (_shutdownCts is null || _shutdownTask is null)
            {
                context.Output("No shutdown is currently scheduled.");
                return;
            }

            _shutdownCts.Cancel();
            _shutdownCts = null;
            _shutdownTask = null;
            _shutdownTime = null;

            // Announce cancellation
            await BroadcastMessage(context, "Server shutdown has been cancelled.");
            context.Output("Shutdown cancelled.");
            return;
        }

        // Check if already scheduled
        if (_shutdownCts is not null && _shutdownTime.HasValue)
        {
            var remaining = _shutdownTime.Value - DateTime.UtcNow;
            if (remaining.TotalSeconds > 0)
            {
                context.Output($"Shutdown already scheduled in {(int)remaining.TotalSeconds} seconds.");
                context.Output("Use 'shutdown cancel' to cancel.");
                return;
            }
        }

        // Parse delay
        int delaySeconds = 0;
        if (args.Length > 0)
        {
            if (!int.TryParse(args[0], out delaySeconds) || delaySeconds < 0)
            {
                context.Output($"Invalid delay: {args[0]}. Use a positive number of seconds.");
                return;
            }
        }

        if (delaySeconds == 0)
        {
            // Immediate shutdown
            context.Output("Initiating immediate shutdown...");
            await BroadcastMessage(context, "*** SERVER SHUTTING DOWN NOW ***");
            await PerformShutdown(context);
        }
        else
        {
            // Delayed shutdown
            _shutdownCts = new CancellationTokenSource();
            _shutdownTime = DateTime.UtcNow.AddSeconds(delaySeconds);

            context.Output($"Shutdown scheduled in {delaySeconds} seconds.");
            await BroadcastMessage(context, $"*** SERVER SHUTTING DOWN IN {delaySeconds} SECONDS ***");

            // Start background shutdown task
            _shutdownTask = RunDelayedShutdown(context, delaySeconds, _shutdownCts.Token);
        }
    }

    private static async Task RunDelayedShutdown(CommandContext context, int totalSeconds, CancellationToken ct)
    {
        var warnings = new[] { 60, 30, 10, 5, 4, 3, 2, 1 };
        var remaining = totalSeconds;

        while (remaining > 0)
        {
            if (ct.IsCancellationRequested)
                return;

            // Check if we should send a warning
            foreach (var warning in warnings)
            {
                if (remaining == warning)
                {
                    await BroadcastMessage(context, $"*** SERVER SHUTTING DOWN IN {warning} SECOND{(warning > 1 ? "S" : "")} ***");
                    break;
                }
            }

            await Task.Delay(1000, ct);
            remaining--;
        }

        if (!ct.IsCancellationRequested)
        {
            await BroadcastMessage(context, "*** SERVER SHUTTING DOWN NOW ***");
            await PerformShutdown(context);
        }
    }

    private static async Task BroadcastMessage(CommandContext context, string message)
    {
        var sessions = context.State.Sessions.GetAll();
        foreach (var session in sessions)
        {
            try
            {
                await session.WriteLineAsync("");
                await session.WriteLineAsync(message);
                await session.WriteLineAsync("");
            }
            catch
            {
                // Session may have disconnected
            }
        }
    }

    private static async Task PerformShutdown(CommandContext context)
    {
        try
        {
            // Save world state
            // Note: This would need access to the persistence layer
            // For now, we just disconnect players gracefully

            // Shutdown daemons
            context.State.ShutdownDaemons();

            // Disconnect all players
            var sessions = context.State.Sessions.GetAll();
            foreach (var session in sessions)
            {
                try
                {
                    await session.WriteLineAsync("Connection closed. Goodbye!");
                    await session.CloseAsync();
                }
                catch
                {
                    // Ignore errors during shutdown
                }
            }

            // Set shutdown flag - the main server loop should check this
            // and exit gracefully. For now, we'll use Environment.Exit
            // which is not ideal but works.
            Console.WriteLine("Server shutdown requested by wizard command.");

            // Give a moment for cleanup
            await Task.Delay(500);

            // Exit the process
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during shutdown: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
