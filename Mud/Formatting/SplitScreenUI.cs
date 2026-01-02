using System.Text;

namespace JitRealm.Mud.Formatting;

/// <summary>
/// Split-screen terminal UI for ANSI-capable telnet clients.
///
/// Layout (for 80x24 terminal):
///   Rows 1 to 22:  Scrolling output area (VT100 scroll region)
///   Row 23:        Status bar (fixed, reverse video)
///   Row 24:        Input line (fixed)
/// </summary>
public sealed class SplitScreenUI : ITerminalUI
{
    private readonly Func<string, Task> _writeAsync;
    private readonly IMudFormatter _formatter;

    public bool SupportsSplitScreen => true;
    public int Width { get; private set; }
    public int Height { get; private set; }

    private int OutputAreaBottom => Height - 2;  // Last row of scroll region
    private int StatusRow => Height - 1;         // Status bar row
    private int InputRow => Height;              // Input line row

    private StatusBarData? _lastStatusData;

    public SplitScreenUI(
        Func<string, Task> writeAsync,
        IMudFormatter formatter,
        int width = 80,
        int height = 24)
    {
        _writeAsync = writeAsync;
        _formatter = formatter;
        Width = Math.Max(40, width);
        Height = Math.Max(10, height);
    }

    /// <summary>
    /// Update terminal dimensions and adjust scroll region / fixed rows accordingly.
    /// </summary>
    public async Task ResizeAsync(int width, int height)
    {
        var newWidth = Math.Max(40, width);
        var newHeight = Math.Max(10, height);

        if (newWidth == Width && newHeight == Height)
            return;

        Width = newWidth;
        Height = newHeight;

        var sb = new StringBuilder();

        // Preserve cursor while we adjust the scroll region.
        sb.Append(AnsiSequences.SaveCursor);
        sb.Append(AnsiSequences.SetScrollRegion(1, OutputAreaBottom));
        sb.Append(AnsiSequences.RestoreCursor);

        await _writeAsync(sb.ToString());

        // Force a status bar redraw so it fills the new width.
        if (_lastStatusData is StatusBarData data)
        {
            _lastStatusData = null;
            await UpdateStatusBarAsync(data);
        }
    }

    public async Task InitializeAsync()
    {
        var sb = new StringBuilder();

        // Clear screen
        sb.Append(AnsiSequences.ClearScreen);

        // Move cursor to top
        sb.Append(AnsiSequences.CursorHome);

        // Set scroll region to output area (leaves status bar and input line fixed)
        sb.Append(AnsiSequences.SetScrollRegion(1, OutputAreaBottom));

        // Position cursor at bottom of output area
        sb.Append(AnsiSequences.CursorTo(OutputAreaBottom, 1));

        // Show cursor
        sb.Append(AnsiSequences.ShowCursor);

        await _writeAsync(sb.ToString());
    }

    public async Task WriteOutputAsync(string text)
    {
        // Handle multi-line text
        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            await WriteOutputLineAsync(line.TrimEnd('\r'));
        }
    }

    public async Task WriteOutputLinesAsync(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            await WriteOutputAsync(line);
        }
    }

    private async Task WriteOutputLineAsync(string text)
    {
        // Word-wrap if needed
        var wrappedLines = WordWrap(text, Width - 1);

        foreach (var line in wrappedLines)
        {
            var sb = new StringBuilder();

            // Save cursor position
            sb.Append(AnsiSequences.SaveCursor);

            // Move to bottom of scroll region
            sb.Append(AnsiSequences.CursorTo(OutputAreaBottom, 1));

            // Write text with newline (causes scroll within region)
            sb.Append(line);
            sb.Append(AnsiSequences.CRLF);

            // Restore cursor position
            sb.Append(AnsiSequences.RestoreCursor);

            await _writeAsync(sb.ToString());
        }
    }

    public async Task UpdateStatusBarAsync(StatusBarData data)
    {
        // Skip if data hasn't changed
        if (_lastStatusData == data)
            return;

        _lastStatusData = data;

        var statusText = BuildStatusBar(data);

        var sb = new StringBuilder();

        // Save cursor
        sb.Append(AnsiSequences.SaveCursor);

        // Move to status row
        sb.Append(AnsiSequences.CursorTo(StatusRow, 1));

        // Clear line and write status in reverse video
        sb.Append(AnsiSequences.ClearLine);
        sb.Append(AnsiSequences.Reverse);
        sb.Append(statusText);
        sb.Append(AnsiSequences.Reset);

        // Restore cursor
        sb.Append(AnsiSequences.RestoreCursor);

        await _writeAsync(sb.ToString());
    }

    public async Task RenderInputLineAsync(string prompt, string currentInput = "")
    {
        var sb = new StringBuilder();

        // Move to input row
        sb.Append(AnsiSequences.CursorTo(InputRow, 1));

        // Clear line
        sb.Append(AnsiSequences.ClearLine);

        // Write prompt and current input
        sb.Append(prompt);
        sb.Append(currentInput);

        await _writeAsync(sb.ToString());
    }

    public async Task ResetTerminalAsync()
    {
        var sb = new StringBuilder();

        // Reset scroll region to full screen
        sb.Append(AnsiSequences.ResetScrollRegion);

        // Clear screen
        sb.Append(AnsiSequences.ClearScreen);

        // Move cursor to top
        sb.Append(AnsiSequences.CursorHome);

        // Show cursor
        sb.Append(AnsiSequences.ShowCursor);

        await _writeAsync(sb.ToString());
    }

    private string BuildStatusBar(StatusBarData data)
    {
        var sb = new StringBuilder();

        // HP color based on percentage
        var hpPercent = data.MaxHP > 0 ? (double)data.HP / data.MaxHP : 0;
        var hpColor = hpPercent switch
        {
            >= 0.75 => AnsiSequences.FgBrightGreen,
            >= 0.50 => AnsiSequences.FgBrightYellow,
            >= 0.25 => AnsiSequences.FgBrightRed,
            _ => AnsiSequences.FgRed
        };

        // Wide format (80+ columns)
        if (Width >= 80)
        {
            sb.Append(' ');
            sb.Append(data.PlayerName);

            if (data.IsWizard)
                sb.Append(" [Wiz]");

            sb.Append(" | ");
            sb.Append(hpColor);
            sb.Append($"HP: {data.HP}/{data.MaxHP}");
            sb.Append(AnsiSequences.Reset);
            sb.Append(AnsiSequences.Reverse);

            sb.Append(" | ");
            sb.Append(data.Location);

            if (data.CombatTarget != null)
            {
                sb.Append(" | Fighting: ");
                sb.Append(data.CombatTarget);

                if (data.TargetHP.HasValue && data.TargetMaxHP.HasValue)
                {
                    sb.Append($" ({data.TargetHP}/{data.TargetMaxHP})");
                }
            }
        }
        else
        {
            // Compact format for narrow terminals
            sb.Append(' ');
            sb.Append(data.PlayerName.Length > 10 ? data.PlayerName[..10] : data.PlayerName);
            sb.Append(' ');
            sb.Append(hpColor);
            sb.Append($"{data.HP}/{data.MaxHP}");
            sb.Append(AnsiSequences.Reset);
            sb.Append(AnsiSequences.Reverse);
            sb.Append(' ');
            sb.Append(data.Location.Length > 15 ? data.Location[..15] : data.Location);

            if (data.CombatTarget != null)
            {
                sb.Append(" [Combat]");
            }
        }

        // Pad to full terminal width (using visible length, not string length)
        var result = sb.ToString();
        var visibleLen = GetVisibleLength(result);

        if (visibleLen < Width)
        {
            // Add padding spaces to fill the rest of the terminal width
            result += new string(' ', Width - visibleLen);
        }

        return result;
    }

    private static IEnumerable<string> WordWrap(string text, int maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield return "";
            yield break;
        }

        // Handle text shorter than max width
        if (GetVisibleLength(text) <= maxWidth)
        {
            yield return text;
            yield break;
        }

        // Simple word wrap - split on spaces
        var words = text.Split(' ');
        var currentLine = new StringBuilder();
        var currentVisibleLength = 0;

        foreach (var word in words)
        {
            var wordVisibleLength = GetVisibleLength(word);

            if (currentVisibleLength + wordVisibleLength + (currentLine.Length > 0 ? 1 : 0) > maxWidth)
            {
                if (currentLine.Length > 0)
                {
                    yield return currentLine.ToString();
                    currentLine.Clear();
                    currentVisibleLength = 0;
                }

                // Handle words longer than maxWidth
                if (wordVisibleLength > maxWidth)
                {
                    yield return word[..maxWidth];
                    continue;
                }
            }

            if (currentLine.Length > 0)
            {
                currentLine.Append(' ');
                currentVisibleLength++;
            }

            currentLine.Append(word);
            currentVisibleLength += wordVisibleLength;
        }

        if (currentLine.Length > 0)
        {
            yield return currentLine.ToString();
        }
    }

    /// <summary>
    /// Get visible length of text, excluding ANSI escape sequences.
    /// </summary>
    private static int GetVisibleLength(string text)
    {
        var length = 0;
        var inEscape = false;

        foreach (var c in text)
        {
            if (c == '\u001b')
            {
                inEscape = true;
                continue;
            }

            if (inEscape)
            {
                if (c == 'm')
                {
                    inEscape = false;
                }
                continue;
            }

            length++;
        }

        return length;
    }
}
