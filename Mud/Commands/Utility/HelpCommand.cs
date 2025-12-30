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
        }
        else
        {
            // Show help for specific command
            var commandName = args[0];
            var help = _registry.GetCommandHelp(commandName);
            if (help is null)
            {
                context.Output($"Unknown command: {commandName}");
            }
            else
            {
                context.Output(help);
            }
        }

        return Task.CompletedTask;
    }
}
