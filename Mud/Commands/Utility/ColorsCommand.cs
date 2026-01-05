using JitRealm.Mud.Formatting;
using Spectre.Console;

namespace JitRealm.Mud.Commands.Utility;

/// <summary>
/// Toggle or test ANSI color support for the session.
/// </summary>
public class ColorsCommand : CommandBase
{
    public override string Name => "colors";
    public override IReadOnlyList<string> Aliases => new[] { "colour", "color" };
    public override string Usage => "colors [on|off|test]";
    public override string Description => "Toggle or test color display";
    public override string Category => "Utility";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        var session = context.Session;
        var fmt = session.Formatter;

        if (args.Length == 0)
        {
            // Show current status
            var status = session.SupportsAnsi ? "enabled" : "disabled";
            context.Output(fmt.FormatInfo($"Colors are currently {status}. Use 'colors on' or 'colors off' to change."));
            return;
        }

        var argument = args[0].ToLowerInvariant();

        switch (argument)
        {
            case "on":
            case "true":
            case "yes":
            case "enable":
                session.SupportsAnsi = true;
                context.Output(session.Formatter.FormatSuccess("Colors enabled."));
                break;

            case "off":
            case "false":
            case "no":
            case "disable":
                session.SupportsAnsi = false;
                context.Output(session.Formatter.FormatInfo("Colors disabled."));
                break;

            case "test":
                await TestColorsAsync(context);
                break;

            default:
                context.Output(fmt.FormatError("Usage: colors on|off|test"));
                break;
        }
    }

    private static async Task TestColorsAsync(CommandContext context)
    {
        var session = context.Session;

        // Output raw ANSI escape codes directly to test terminal support
        await session.WriteLineAsync("Testing ANSI color support...");
        await session.WriteLineAsync("");

        // Raw ANSI codes (ESC = \x1b = \u001b)
        await session.WriteLineAsync("Raw ANSI escape codes:");
        await session.WriteLineAsync("\u001b[31mThis should be RED\u001b[0m");
        await session.WriteLineAsync("\u001b[32mThis should be GREEN\u001b[0m");
        await session.WriteLineAsync("\u001b[33mThis should be YELLOW\u001b[0m");
        await session.WriteLineAsync("\u001b[34mThis should be BLUE\u001b[0m");
        await session.WriteLineAsync("\u001b[1;35mThis should be BOLD MAGENTA\u001b[0m");
        await session.WriteLineAsync("");

        // Spectre.Console generated (real Spectre rendering to session output)
        await session.WriteLineAsync("Spectre.Console generated (table/panel):");
        var spectre = SpectreSessionRenderer.Render(console =>
        {
            console.MarkupLine("[bold yellow]Spectre markup[/] [green]works[/] if ANSI is enabled.");
            var panel = new Panel("This is a [blue]Panel[/].")
                .Header("Spectre.Console");
            // Full-featured Spectre output (Unicode box drawing).
            panel.Border = BoxBorder.Rounded;
            console.Write(panel);

            var table = new Table();
            table.AddColumn("Key");
            table.AddColumn("Value");
            table.Border(TableBorder.Rounded);
            table.AddRow("SupportsAnsi", session.SupportsAnsi.ToString());
            table.AddRow("Formatter", session.Formatter.GetType().Name);
            table.AddRow("SessionId", session.SessionId);
            console.Write(table);
        }, new SpectreRenderOptions(
            EnableAnsi: session.SupportsAnsi,
            EnableUnicode: true,
            Width: session.TerminalSize.Width,
            Height: session.TerminalSize.Height,
            ColorSystem: ColorSystemSupport.Standard));

        await session.WriteAsync(spectre);
        await session.WriteLineAsync("");

        // Show formatter type
        await session.WriteLineAsync($"Current formatter: {session.Formatter.GetType().Name}");
        await session.WriteLineAsync($"SupportsAnsi: {session.SupportsAnsi}");
    }
}
