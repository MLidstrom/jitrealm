using System.Text;
using Spectre.Console;

namespace JitRealm.Mud.Formatting;

/// <summary>
/// Rendering options for Spectre.Console when outputting to non-console sessions (e.g. telnet).
/// </summary>
public readonly record struct SpectreRenderOptions(
    bool EnableAnsi,
    bool EnableUnicode = true,
    int? Width = null,
    int? Height = null,
    ColorSystemSupport? ColorSystem = null);

/// <summary>
/// Renders Spectre.Console output to a string for non-console sessions (e.g. telnet).
/// This avoids Spectre writing to the host process console and lets us forward the
/// rendered ANSI/Unicode output to the session's output stream.
/// </summary>
public static class SpectreSessionRenderer
{
    public static string Render(Action<IAnsiConsole> render, bool enableAnsi)
        => Render(render, new SpectreRenderOptions(enableAnsi));

    public static string Render(Action<IAnsiConsole> render, SpectreRenderOptions options)
    {
        var sw = new StringWriter();

        var colorSystemSupport = options.EnableAnsi
            ? (options.ColorSystem ?? ColorSystemSupport.Standard)
            : ColorSystemSupport.NoColors;

        // For telnet-style output, keep this non-interactive and deterministic.
        // We explicitly set Profile width/height/capabilities after creation.
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = options.EnableAnsi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = colorSystemSupport,
            Interactive = InteractionSupport.No,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            Out = new AnsiConsoleOutput(sw)
        });

        // Make Spectre layout match the remote client's terminal.
        // (This affects wrapping/truncation of tables/panels etc.)
        if (options.Width is > 0)
            console.Profile.Width = options.Width.Value;
        if (options.Height is > 0)
            console.Profile.Height = options.Height.Value;

        // Telnet sessions use UTF-8; keep Spectre consistent so box drawing works when enabled.
        console.Profile.Encoding = Encoding.UTF8;

        // Be conservative: assume no interactivity, no links, no alternate buffer.
        console.Profile.Capabilities.Ansi = options.EnableAnsi;
        console.Profile.Capabilities.Unicode = options.EnableUnicode;
        console.Profile.Capabilities.Interactive = false;
        console.Profile.Capabilities.Links = false;
        console.Profile.Capabilities.AlternateBuffer = false;
        console.Profile.Capabilities.ColorSystem = ToColorSystem(colorSystemSupport);

        render(console);

        var output = sw.ToString();
        output = NormalizeLineEndingsForTelnet(output);

        // Spectre can still emit a small amount of control sequences even when ANSI is "disabled"
        // depending on widgets used. For telnet/plain clients, strip them defensively.
        if (!options.EnableAnsi && output.Contains('\u001b'))
            output = StripAnsiEscapeSequences(output);

        // Defensive: if ANSI is enabled and we emitted control codes, ensure we end in a sane state.
        if (options.EnableAnsi && output.Contains('\u001b') && !EndsWithResetIgnoringTrailingCrlf(output))
            output = InsertResetBeforeTrailingCrlf(output);

        return output;
    }

    private static ColorSystem ToColorSystem(ColorSystemSupport support) => support switch
    {
        ColorSystemSupport.TrueColor => ColorSystem.TrueColor,
        ColorSystemSupport.EightBit => ColorSystem.EightBit,
        ColorSystemSupport.Standard => ColorSystem.Standard,
        ColorSystemSupport.NoColors => ColorSystem.NoColors,
        // Detect shouldn't generally be used for telnet rendering; fall back safely.
        _ => ColorSystem.Standard
    };

    // Telnet expects CRLF. Spectre mostly writes \n.
    private static string NormalizeLineEndingsForTelnet(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Normalize to \n first, then convert to \r\n.
        text = text.Replace("\r\n", "\n");
        text = text.Replace("\r", "\n");
        return text.Replace("\n", "\r\n");
    }

    private static bool EndsWithResetIgnoringTrailingCrlf(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        while (text.EndsWith("\r\n", StringComparison.Ordinal))
            text = text[..^2];

        return text.EndsWith("\u001b[0m", StringComparison.Ordinal);
    }

    private static string InsertResetBeforeTrailingCrlf(string text)
    {
        const string Reset = "\u001b[0m";

        var suffix = "";
        while (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            text = text[..^2];
            suffix += "\r\n";
        }

        return text + Reset + suffix;
    }

    /// <summary>
    /// Strips ANSI escape sequences (CSI/OSC) from text.
    /// Useful as a safety net when ANSI output is disabled.
    /// </summary>
    private static string StripAnsiEscapeSequences(string text)
    {
        var sb = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\u001b')
            {
                sb.Append(c);
                continue;
            }

            // ESC [
            if (i + 1 < text.Length && text[i + 1] == '[')
            {
                i += 2;
                // Consume until final byte (@..~) or end.
                while (i < text.Length)
                {
                    var ch = text[i];
                    if (ch >= '@' && ch <= '~')
                        break;
                    i++;
                }
                continue;
            }

            // ESC ] ... BEL or ESC \
            if (i + 1 < text.Length && text[i + 1] == ']')
            {
                i += 2;
                while (i < text.Length)
                {
                    if (text[i] == '\a') // BEL
                        break;

                    if (text[i] == '\u001b' && i + 1 < text.Length && text[i + 1] == '\\')
                    {
                        i++; // consume '\' via loop increment
                        break;
                    }

                    i++;
                }
                continue;
            }

            // Unknown escape: drop the ESC and continue.
        }

        return sb.ToString();
    }
}
