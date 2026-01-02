using System.Text;

namespace JitRealm.Mud.Formatting;

/// <summary>
/// Renders the centered welcome screen with ASCII art banner.
/// </summary>
public static class WelcomeScreen
{
    private static readonly string[] AsciiArt =
    [
        @"      ___ _ _   ____            _           ",
        @"     |_  (_) | |  _ \ ___  __ _| |_ __ ___  ",
        @"       | | | __| |_) / _ \/ _` | | '_ ` _ \ ",
        @"    /\__/ / | |_|  _ <  __/ (_| | | | | | | |",
        @"    \____/|_|\__|_| \_\___|\__,_|_|_| |_| |_|"
    ];

    /// <summary>
    /// Render the welcome screen with centered ASCII art banner.
    /// </summary>
    public static async Task RenderAsync(
        Func<string, Task> writeAsync,
        string version,
        int termWidth = 80,
        int termHeight = 24,
        bool supportsAnsi = true)
    {
        var sb = new StringBuilder();

        if (supportsAnsi)
        {
            // Clear screen and move cursor to top
            sb.Append(AnsiSequences.ClearScreen);
            sb.Append(AnsiSequences.CursorHome);
        }

        // Calculate vertical centering
        // Art height + version line + 2 blank lines + prompt area (approx 4 lines)
        var totalLines = AsciiArt.Length + 6;
        var startRow = Math.Max(1, (termHeight - totalLines) / 2);

        if (supportsAnsi)
        {
            sb.Append(AnsiSequences.CursorTo(startRow, 1));
        }
        else
        {
            // Add blank lines for non-ANSI centering
            for (var i = 0; i < startRow - 1; i++)
            {
                sb.Append(AnsiSequences.CRLF);
            }
        }

        await writeAsync(sb.ToString());
        sb.Clear();

        // Render ASCII art centered and in cyan
        foreach (var line in AsciiArt)
        {
            var padding = Math.Max(0, (termWidth - line.Length) / 2);
            var centeredLine = new string(' ', padding) + line;

            if (supportsAnsi)
            {
                sb.Append(AnsiSequences.FgBrightCyan);
                sb.Append(centeredLine);
                sb.Append(AnsiSequences.Reset);
            }
            else
            {
                sb.Append(centeredLine);
            }

            sb.Append(AnsiSequences.CRLF);
        }

        // Blank line
        sb.Append(AnsiSequences.CRLF);

        // Version line centered
        var versionLine = $"v{version}";
        var versionPad = Math.Max(0, (termWidth - versionLine.Length) / 2);

        if (supportsAnsi)
        {
            sb.Append(AnsiSequences.FgBrightBlack);
            sb.Append(new string(' ', versionPad));
            sb.Append(versionLine);
            sb.Append(AnsiSequences.Reset);
        }
        else
        {
            sb.Append(new string(' ', versionPad));
            sb.Append(versionLine);
        }

        sb.Append(AnsiSequences.CRLF);
        sb.Append(AnsiSequences.CRLF);

        await writeAsync(sb.ToString());
    }

    /// <summary>
    /// Render just the login/create prompt below the welcome screen.
    /// </summary>
    public static async Task RenderLoginPromptAsync(
        Func<string, Task> writeAsync,
        Func<string, Task> writeLineAsync,
        bool showCreateHint = false)
    {
        if (showCreateHint)
        {
            await writeLineAsync("No player accounts found. Choose (C) to create your first character.");
            await writeLineAsync("");
        }

        await writeLineAsync("(L)ogin or (C)reate new player?");
        await writeAsync("> ");
    }
}
