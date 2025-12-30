using JitRealm.Mud.Commands.Social;
using JitRealm.Mud.Commands.Utility;
using JitRealm.Mud.Commands.Wizard;

namespace JitRealm.Mud.Commands;

/// <summary>
/// Factory for creating and registering all commands.
/// </summary>
public static class CommandFactory
{
    /// <summary>
    /// Create a command registry with all standard commands registered.
    /// </summary>
    public static CommandRegistry CreateRegistry()
    {
        var registry = new CommandRegistry();

        // Register social commands
        RegisterSocialCommands(registry);

        // Register utility commands
        RegisterUtilityCommands(registry);

        // Register wizard commands
        RegisterWizardCommands(registry);

        return registry;
    }

    private static void RegisterSocialCommands(CommandRegistry registry)
    {
        // Basic social
        registry.Register(new ShoutCommand());
        registry.Register(new WhisperCommand());
        registry.Register(new WhoCommand());

        // Predefined emotes
        registry.Register(new BowCommand());
        registry.Register(new WaveCommand());
        registry.Register(new LaughCommand());
        registry.Register(new SmileCommand());
        registry.Register(new NodCommand());
        registry.Register(new ShakeCommand());
        registry.Register(new ShrugCommand());
        registry.Register(new SighCommand());
        registry.Register(new CheerCommand());
        registry.Register(new ThinkCommand());
        registry.Register(new CryCommand());
        registry.Register(new DanceCommand());
        registry.Register(new YawnCommand());
    }

    private static void RegisterUtilityCommands(CommandRegistry registry)
    {
        // Help command needs the registry reference
        registry.Register(new HelpCommand(registry));
        registry.Register(new TimeCommand());
        registry.Register(new ScoreCommand());
    }

    private static void RegisterWizardCommands(CommandRegistry registry)
    {
        registry.Register(new BlueprintsCommand());
        registry.Register(new ObjectsCommand());
        registry.Register(new ReloadCommand());
        registry.Register(new UnloadCommand());
        registry.Register(new CloneCommand());
        registry.Register(new DestructCommand());
        registry.Register(new StatCommand());
        registry.Register(new ResetCommand());
        registry.Register(new PatchCommand());
    }
}
