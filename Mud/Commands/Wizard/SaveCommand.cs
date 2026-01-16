namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// Wizard command to save the world state to disk.
/// </summary>
public class SaveCommand : WizardCommandBase
{
    public override string Name => "save";
    public override string[] Aliases => Array.Empty<string>();
    public override string Usage => "save";
    public override string Description => "Save world state to disk";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (context.State.Persistence is null)
        {
            context.Output("Persistence system is not available.");
            return;
        }

        try
        {
            await context.State.Persistence.SaveAsync(context.State);
            context.Output("World state saved.");
        }
        catch (Exception ex)
        {
            context.Output($"Failed to save world state: {ex.Message}");
        }
    }
}
