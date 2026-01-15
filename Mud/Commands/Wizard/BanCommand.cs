namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to ban players from the server.
/// </summary>
public class BanCommand : WizardCommandBase
{
    public override string Name => "ban";
    public override string[] Aliases => Array.Empty<string>();
    public override string Usage => "ban <player> [reason] | banlist";
    public override string Description => "Ban a player from connecting";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage(context);
            return;
        }

        // Check for banlist subcommand
        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("banlist", StringComparison.OrdinalIgnoreCase))
        {
            ShowBanList(context);
            return;
        }

        var playerName = args[0];
        var reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;

        // Check if BanManager exists
        if (context.State.BanManager is null)
        {
            context.Output("Ban system is not available.");
            return;
        }

        // Check if already banned
        if (context.State.BanManager.IsBanned(playerName))
        {
            context.Output($"{playerName} is already banned.");
            return;
        }

        // Prevent banning self
        if (playerName.Equals(context.Session.PlayerName, StringComparison.OrdinalIgnoreCase))
        {
            context.Output("You cannot ban yourself.");
            return;
        }

        // Ban the player
        context.State.BanManager.Ban(playerName, context.Session.PlayerName ?? "Unknown", reason);
        context.Output($"Banned {playerName}. Reason: {reason ?? "No reason given"}");

        // If the player is online, disconnect them
        var session = context.State.Sessions.GetByPlayerName(playerName);
        if (session is not null)
        {
            await session.WriteLineAsync($"You have been banned by {context.Session.PlayerName}.");
            if (reason is not null)
            {
                await session.WriteLineAsync($"Reason: {reason}");
            }
            await session.CloseAsync();
            context.Output($"{playerName} was online and has been disconnected.");
        }
    }

    private void ShowUsage(CommandContext context)
    {
        context.Output("Usage: ban <player> [reason]");
        context.Output("       ban list");
        context.Output("");
        context.Output("Examples:");
        context.Output("  ban alice Repeated griefing");
        context.Output("  ban bob");
        context.Output("  ban list");
    }

    private void ShowBanList(CommandContext context)
    {
        if (context.State.BanManager is null)
        {
            context.Output("Ban system is not available.");
            return;
        }

        var bans = context.State.BanManager.GetAllBans();

        if (bans.Count == 0)
        {
            context.Output("No players are currently banned.");
            return;
        }

        context.Output($"Banned Players ({bans.Count}):");
        context.Output("");

        foreach (var ban in bans.OrderBy(b => b.BannedAt))
        {
            context.Output($"  {ban.PlayerName}");
            context.Output($"    Banned by: {ban.BannedBy}");
            context.Output($"    Reason: {ban.Reason}");
            context.Output($"    Date: {ban.BannedAt:yyyy-MM-dd HH:mm} UTC");
            context.Output("");
        }
    }
}

/// <summary>
/// Wizard command to unban players.
/// </summary>
public class UnbanCommand : WizardCommandBase
{
    public override string Name => "unban";
    public override string[] Aliases => Array.Empty<string>();
    public override string Usage => "unban <player>";
    public override string Description => "Remove a player's ban";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1))
            return Task.CompletedTask;

        var playerName = args[0];

        if (context.State.BanManager is null)
        {
            context.Output("Ban system is not available.");
            return Task.CompletedTask;
        }

        if (!context.State.BanManager.IsBanned(playerName))
        {
            context.Output($"{playerName} is not banned.");
            return Task.CompletedTask;
        }

        context.State.BanManager.Unban(playerName);
        context.Output($"Unbanned {playerName}. They may now connect.");

        return Task.CompletedTask;
    }
}
