using System.Text;
using Spectre.Console;

namespace JitRealm.Mud.Formatting;

/// <summary>
/// Renders Spectre.Console output to a string for non-console sessions (e.g. telnet).
/// This avoids Spectre writing to the host process console and lets us forward the
/// rendered ANSI/Unicode output to the session's output stream.
/// </summary>
public static class SpectreSessionRenderer
{
    public static string Render(Action<IAnsiConsole> render, bool enableAnsi)
    {
        var sw = new StringWriter();

        // Note: ColorSystemSupport.TrueColor is safe; terminals that don't support it will still show output,
        // but may degrade colors. This is mainly for testing; production can tune per-session later.
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = enableAnsi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = enableAnsi ? ColorSystemSupport.TrueColor : ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(sw)
        });

        render(console);
        return NormalizeLineEndingsForTelnet(sw.ToString());
    }

    // Telnet expects CRLF. Spectre writes \n.
    private static string NormalizeLineEndingsForTelnet(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Normalize to \n first, then convert to \r\n.
        text = text.Replace("\r\n", "\n");
        return text.Replace("\n", "\r\n");
    }
}


