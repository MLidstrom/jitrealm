using System.Text;

namespace JitRealm.Mud.Commands.Utility;

/// <summary>
/// Display help for commands.
/// </summary>
public class HelpCommand : CommandBase
{
    private readonly CommandRegistry _registry;

    public HelpCommand(CommandRegistry registry)
    {
        _registry = registry;
    }

    public override string Name => "help";
    public override IReadOnlyList<string> Aliases => new[] { "?" };
    public override string Usage => "help [command]";
    public override string Description => "Show help for commands";
    public override string Category => "Utility";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            // Show general help
            var summary = _registry.GetHelpSummary(context.IsWizard);
            context.Output(summary);

            // Show local commands if any
            var localDispatcher = new LocalCommandDispatcher(context.State);
            var localCommands = localDispatcher.GetAvailableCommands(context.PlayerId).ToList();
            if (localCommands.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("Local Commands (context-sensitive):");
                foreach (var (source, cmd) in localCommands)
                {
                    sb.AppendLine($"  {cmd.Usage,-20} - {cmd.Description} [{source}]");
                }
                context.Output(sb.ToString().TrimEnd());
            }
        }
        else
        {
            // Show help for specific command
            var commandName = args[0];
            var help = _registry.GetCommandHelp(commandName);
            if (help is null)
            {
                // Try local command help
                var localDispatcher = new LocalCommandDispatcher(context.State);
                var localHelp = localDispatcher.GetCommandHelp(context.PlayerId, commandName);
                if (localHelp is not null)
                {
                    context.Output(localHelp);
                }
                else
                {
                    context.Output($"Unknown command: {commandName}");
                }
            }
            else
            {
                context.Output(help);
            }
        }

        return Task.CompletedTask;
    }
}
