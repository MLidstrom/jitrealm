using JitRealm.Mud.Commands.Combat;
using JitRealm.Mud.Commands.Equipment;
using JitRealm.Mud.Commands.Inventory;
using JitRealm.Mud.Commands.Navigation;
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

        // Register navigation commands
        RegisterNavigationCommands(registry);

        // Register inventory commands
        RegisterInventoryCommands(registry);

        // Register equipment commands
        RegisterEquipmentCommands(registry);

        // Register combat commands
        RegisterCombatCommands(registry);

        // Register social commands
        RegisterSocialCommands(registry);

        // Register utility commands
        RegisterUtilityCommands(registry);

        // Register wizard commands
        RegisterWizardCommands(registry);

        return registry;
    }

    private static void RegisterNavigationCommands(CommandRegistry registry)
    {
        registry.Register(new LookCommand());
        registry.Register(new GoCommand());
    }

    private static void RegisterInventoryCommands(CommandRegistry registry)
    {
        registry.Register(new GetCommand());
        registry.Register(new DropCommand());
        registry.Register(new GiveCommand());
        registry.Register(new InventoryCommand());
        registry.Register(new ExamineCommand());
        registry.Register(new EatCommand());
        registry.Register(new DrinkCommand());
    }

    private static void RegisterEquipmentCommands(CommandRegistry registry)
    {
        registry.Register(new EquipCommand());
        registry.Register(new UnequipCommand());
        registry.Register(new EquipmentCommand());
    }

    private static void RegisterCombatCommands(CommandRegistry registry)
    {
        registry.Register(new KillCommand());
        registry.Register(new FleeCommand());
        registry.Register(new ConsiderCommand());
    }

    private static void RegisterSocialCommands(CommandRegistry registry)
    {
        // Basic social
        registry.Register(new SayCommand());
        registry.Register(new EmoteCommand());
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
        registry.Register(new ReadCommand());
        registry.Register(new ExchangeCommand());
        registry.Register(new ColorsCommand());
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
        registry.Register(new PerfCommand());
        registry.Register(new GotoCommand());
        registry.Register(new PwdCommand());
        registry.Register(new LsCommand());
        registry.Register(new CdCommand());
        registry.Register(new CatCommand());
        registry.Register(new MoreCommand());
        registry.Register(new EditCommand());
        registry.Register(new LeditCommand());
        registry.Register(new WhereCommand());
        registry.Register(new GoalCommand());
        registry.Register(new KbCommand());
        registry.Register(new StoryCommand());
        registry.Register(new TraceCommand());
        registry.Register(new CreateCommand());
        registry.Register(new ForceCommand());
        registry.Register(new LinkCommand());
        registry.Register(new UnlinkCommand());
        registry.Register(new DigCommand());

        // New wizard commands (v0.28)
        registry.Register(new HealCommand());
        registry.Register(new ZapCommand());
        registry.Register(new EchoCommand());
        registry.Register(new MoveCommand());
        registry.Register(new SummonCommand());
        registry.Register(new UsersCommand());
        registry.Register(new BanCommand());
        registry.Register(new UnbanCommand());
        registry.Register(new ShutdownCommand());

        // Note: save/load commands are handled directly in CommandLoop
        // because they require access to _persistence which isn't in CommandContext
    }
}
