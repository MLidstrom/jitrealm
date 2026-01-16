namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to restore the world state from disk.
/// </summary>
public class LoadCommand : WizardCommandBase
{
    public override string Name => "load";
    public override string[] Aliases => Array.Empty<string>();
    public override string Usage => "load";
    public override string Description => "Restore world state from disk";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (context.State.Persistence is null)
        {
            context.Output("Persistence system is not available.");
            return;
        }

        try
        {
            var loaded = await context.State.Persistence.LoadAsync(context.State);
            if (loaded)
            {
                context.Output("World state loaded.");
                context.Output("Note: You may need to use 'look' to refresh your view.");
            }
            else
            {
                context.Output("No saved world state found.");
            }
        }
        catch (Exception ex)
        {
            context.Output($"Failed to load world state: {ex.Message}");
        }
    }
}
