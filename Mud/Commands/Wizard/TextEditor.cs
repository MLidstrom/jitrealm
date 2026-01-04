using System.Text;
using JitRealm.Mud.Network;

namespace JitRealm.Mud.Commands.Wizard;

/// <summary>
/// ANSI-based text editor for in-game file editing.
/// Provides nano-style editing for wizards.
/// </summary>
public class TextEditor
{
    // ANSI escape codes
    private const string Esc = "\x1b";
    private const string ClearScreen = "\x1b[2J";
    private const string ClearLine = "\x1b[K";
    private const string CursorHome = "\x1b[H";
    private const string HideCursor = "\x1b[?25l";
    private const string ShowCursor = "\x1b[?25h";
    private const string ReverseVideo = "\x1b[7m";
    private const string ResetAttr = "\x1b[0m";
    private const string SaveCursor = "\x1b[s";
    private const string RestoreCursor = "\x1b[u";

    private readonly ISession _session;
    private readonly string _filePath;
    private readonly string _virtualPath;
    private readonly List<StringBuilder> _lines;
    private int _cursorRow;
    private int _cursorCol;
    private int _viewportTop;
    private int _viewportLeft;
    private bool _modified;
    private bool _running;
    private int _screenWidth;
    private int _screenHeight;
    private string _statusMessage = "";
    private DateTime _statusTime = DateTime.MinValue;

    // Constants for screen layout
    private const int HeaderLines = 1;
    private const int FooterLines = 2;

    public TextEditor(ISession session, string filePath, string virtualPath, string[] content)
    {
        _session = session;
        _filePath = filePath;
        _virtualPath = virtualPath;
        _lines = content.Length > 0
            ? content.Select(l => new StringBuilder(l)).ToList()
            : new List<StringBuilder> { new StringBuilder() };

        UpdateScreenSize();
    }

    private void UpdateScreenSize()
    {
        _screenWidth = _session.TerminalSize.Width;
        _screenHeight = _session.TerminalSize.Height;
    }

    private int ContentHeight => Math.Max(1, _screenHeight - HeaderLines - FooterLines);

    /// <summary>
    /// Run the editor. Returns true if file was saved.
    /// </summary>
    public async Task<bool> RunAsync(Func<Task<char?>> readChar, CancellationToken ct = default)
    {
        _running = true;
        var saved = false;

        // Enable character-at-a-time mode for telnet clients
        // This disables local echo and line buffering so we get each keystroke
        if (_session is TelnetSession telnetSession)
        {
            telnetSession.EnableCharacterMode();
        }

        try
        {
            // Enter alternate screen buffer and hide cursor during redraw
            await WriteRawAsync($"{Esc}[?1049h"); // Enter alternate screen
            await FullRedraw();

            while (_running && !ct.IsCancellationRequested)
            {
                // Check for terminal resize
                var (newWidth, newHeight) = _session.TerminalSize;
                if (newWidth != _screenWidth || newHeight != _screenHeight)
                {
                    _screenWidth = newWidth;
                    _screenHeight = newHeight;
                    EnsureCursorVisible();
                    await FullRedraw();
                }

                var ch = await readChar();
                if (ch is null)
                {
                    await Task.Delay(10, ct);
                    continue;
                }

                if (ch.Value == '\x1b') // Escape sequence
                {
                    await HandleEscapeSequence(readChar);
                }
                else if (ch.Value < 32) // Control character
                {
                    saved = await HandleControlChar(ch.Value);
                }
                else // Regular character
                {
                    InsertChar(ch.Value);
                    await RefreshCurrentLine();
                    await UpdateCursor();
                }

                // Clear status message after 3 seconds
                if (_statusMessage.Length > 0 && DateTime.Now - _statusTime > TimeSpan.FromSeconds(3))
                {
                    _statusMessage = "";
                    await DrawStatusBar();
                }
            }
        }
        finally
        {
            // Exit alternate screen buffer
            await WriteRawAsync($"{Esc}[?1049l{ShowCursor}");

            // Only disable character mode if line edit mode is not active.
            // Line edit mode depends on character mode for command history to work.
            if (_session is TelnetSession ts && !ts.IsLineEditModeEnabled)
            {
                ts.DisableCharacterMode();
            }
        }

        return saved;
    }

    private async Task HandleEscapeSequence(Func<Task<char?>> readChar)
    {
        // Use longer timeout (100ms) to ensure we get the full sequence
        var next = await ReadWithTimeout(readChar, 100);
        if (next is null) return;

        // Handle both CSI sequences (ESC [) and SS3 sequences (ESC O)
        // SS3 is used by some terminals for application mode arrow keys
        bool isCsi = next == '[';
        bool isSs3 = next == 'O';

        if (!isCsi && !isSs3) return;

        var code = await ReadWithTimeout(readChar, 100);
        if (code is null) return;

        switch (code)
        {
            case 'A': // Up arrow
                if (MoveCursor(-1, 0))
                    await FullRedraw();
                else
                    await UpdateCursor();
                break;
            case 'B': // Down arrow
                if (MoveCursor(1, 0))
                    await FullRedraw();
                else
                    await UpdateCursor();
                break;
            case 'C': // Right arrow
                if (MoveCursor(0, 1))
                    await FullRedraw();
                else
                    await UpdateCursor();
                break;
            case 'D': // Left arrow
                if (MoveCursor(0, -1))
                    await FullRedraw();
                else
                    await UpdateCursor();
                break;
            case 'H': // Home
                {
                    var oldViewportLeft = _viewportLeft;
                    _cursorCol = 0;
                    EnsureCursorVisible();
                    if (_viewportLeft != oldViewportLeft)
                        await FullRedraw();
                    else
                        await UpdateCursor();
                }
                break;
            case 'F': // End
                {
                    var oldViewportLeft = _viewportLeft;
                    _cursorCol = CurrentLine.Length;
                    EnsureCursorVisible();
                    if (_viewportLeft != oldViewportLeft)
                        await FullRedraw();
                    else
                        await UpdateCursor();
                }
                break;
            case '3': // Delete key (sends \x1b[3~)
                if (isCsi)
                {
                    var tilde = await ReadWithTimeout(readChar, 100);
                    if (tilde == '~')
                    {
                        DeleteCharForward();
                        await RefreshCurrentLine();
                        await UpdateCursor();
                    }
                }
                break;
            case '5': // Page Up (\x1b[5~)
                if (isCsi)
                {
                    var tilde = await ReadWithTimeout(readChar, 100);
                    if (tilde == '~')
                    {
                        PageUp();
                        await FullRedraw();
                    }
                }
                break;
            case '6': // Page Down (\x1b[6~)
                if (isCsi)
                {
                    var tilde = await ReadWithTimeout(readChar, 100);
                    if (tilde == '~')
                    {
                        PageDown();
                        await FullRedraw();
                    }
                }
                break;
        }
    }

    private async Task<char?> ReadWithTimeout(Func<Task<char?>> readChar, int ms)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < deadline)
        {
            var ch = await readChar();
            if (ch.HasValue)
                return ch;
            await Task.Delay(5); // Small delay to avoid busy-waiting
        }
        return null;
    }

    private async Task<bool> HandleControlChar(char ch)
    {
        switch (ch)
        {
            case '\x03': // Ctrl+C - Cancel/Exit immediately
                _running = false;
                return false;

            case '\x18': // Ctrl+X - Exit
                if (_modified)
                {
                    SetStatus("Modified buffer! Ctrl+X again to discard, Ctrl+O to save");
                    await DrawStatusBar();
                    // Wait for next key
                    return false;
                }
                _running = false;
                return false;

            case '\x0F': // Ctrl+O - Save
                await SaveFile();
                return true;

            case '\x0B': // Ctrl+K - Cut line
                CutLine();
                await FullRedraw();
                break;

            case '\x07': // Ctrl+G - Help
                SetStatus("^X Exit | ^O Save | ^K Cut | Enter Newline | Arrows Navigate");
                await DrawStatusBar();
                break;

            case '\r': // Enter
            case '\n':
                InsertNewline();
                await FullRedraw();
                break;

            case '\b': // Backspace
            case (char)127: // DEL (some terminals)
                DeleteCharBackward();
                await RefreshCurrentLine();
                await UpdateCursor();
                break;

            case '\t': // Tab - insert spaces
                for (int i = 0; i < 4; i++)
                    InsertChar(' ');
                await RefreshCurrentLine();
                await UpdateCursor();
                break;
        }

        return false;
    }

    private StringBuilder CurrentLine => _lines[_cursorRow];

    /// <summary>
    /// Move the cursor by the given delta. Returns true if viewport scrolled.
    /// </summary>
    private bool MoveCursor(int rowDelta, int colDelta)
    {
        var oldViewportTop = _viewportTop;
        var oldViewportLeft = _viewportLeft;

        _cursorRow = Math.Clamp(_cursorRow + rowDelta, 0, _lines.Count - 1);

        if (colDelta != 0)
        {
            var newCol = _cursorCol + colDelta;

            if (newCol < 0 && _cursorRow > 0)
            {
                // Wrap to end of previous line
                _cursorRow--;
                _cursorCol = CurrentLine.Length;
            }
            else if (newCol > CurrentLine.Length && _cursorRow < _lines.Count - 1)
            {
                // Wrap to start of next line
                _cursorRow++;
                _cursorCol = 0;
            }
            else
            {
                _cursorCol = Math.Clamp(newCol, 0, CurrentLine.Length);
            }
        }
        else
        {
            // Moving vertically - try to maintain column position
            _cursorCol = Math.Min(_cursorCol, CurrentLine.Length);
        }

        EnsureCursorVisible();

        // Return true if viewport scrolled
        return _viewportTop != oldViewportTop || _viewportLeft != oldViewportLeft;
    }

    private void PageUp()
    {
        var jump = ContentHeight - 1;
        _cursorRow = Math.Max(0, _cursorRow - jump);
        _cursorCol = Math.Min(_cursorCol, CurrentLine.Length);
        EnsureCursorVisible();
    }

    private void PageDown()
    {
        var jump = ContentHeight - 1;
        _cursorRow = Math.Min(_lines.Count - 1, _cursorRow + jump);
        _cursorCol = Math.Min(_cursorCol, CurrentLine.Length);
        EnsureCursorVisible();
    }

    private void EnsureCursorVisible()
    {
        // Vertical scrolling
        if (_cursorRow < _viewportTop)
            _viewportTop = _cursorRow;
        else if (_cursorRow >= _viewportTop + ContentHeight)
            _viewportTop = _cursorRow - ContentHeight + 1;

        // Horizontal scrolling
        var lineNumWidth = 5; // "999: "
        var textWidth = _screenWidth - lineNumWidth;

        if (_cursorCol < _viewportLeft)
            _viewportLeft = _cursorCol;
        else if (_cursorCol >= _viewportLeft + textWidth)
            _viewportLeft = _cursorCol - textWidth + 1;
    }

    private void InsertChar(char ch)
    {
        // Safety: never insert control characters (< 32) except handled ones
        // This prevents escape sequences from corrupting the document
        if (ch < 32)
            return;

        CurrentLine.Insert(_cursorCol, ch);
        _cursorCol++;
        _modified = true;
        EnsureCursorVisible();
    }

    private void InsertNewline()
    {
        var rest = CurrentLine.ToString(_cursorCol, CurrentLine.Length - _cursorCol);
        CurrentLine.Remove(_cursorCol, CurrentLine.Length - _cursorCol);

        _cursorRow++;
        _lines.Insert(_cursorRow, new StringBuilder(rest));
        _cursorCol = 0;
        _viewportLeft = 0;
        _modified = true;
        EnsureCursorVisible();
    }

    private void DeleteCharBackward()
    {
        if (_cursorCol > 0)
        {
            CurrentLine.Remove(_cursorCol - 1, 1);
            _cursorCol--;
            _modified = true;
        }
        else if (_cursorRow > 0)
        {
            // Join with previous line
            var prevLine = _lines[_cursorRow - 1];
            var joinCol = prevLine.Length;
            prevLine.Append(CurrentLine);
            _lines.RemoveAt(_cursorRow);
            _cursorRow--;
            _cursorCol = joinCol;
            _modified = true;
        }
        EnsureCursorVisible();
    }

    private void DeleteCharForward()
    {
        if (_cursorCol < CurrentLine.Length)
        {
            CurrentLine.Remove(_cursorCol, 1);
            _modified = true;
        }
        else if (_cursorRow < _lines.Count - 1)
        {
            // Join with next line
            CurrentLine.Append(_lines[_cursorRow + 1]);
            _lines.RemoveAt(_cursorRow + 1);
            _modified = true;
        }
    }

    private void CutLine()
    {
        if (_lines.Count > 1)
        {
            _lines.RemoveAt(_cursorRow);
            if (_cursorRow >= _lines.Count)
                _cursorRow = _lines.Count - 1;
        }
        else
        {
            _lines[0].Clear();
        }
        _cursorCol = Math.Min(_cursorCol, CurrentLine.Length);
        _modified = true;
        EnsureCursorVisible();
    }

    private async Task SaveFile()
    {
        try
        {
            var content = string.Join("\n", _lines.Select(l => l.ToString()));
            await File.WriteAllTextAsync(_filePath, content);
            _modified = false;
            SetStatus($"Saved {_lines.Count} lines to {_virtualPath}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving: {ex.Message}");
        }
        await DrawStatusBar();
    }

    private void SetStatus(string message)
    {
        _statusMessage = message;
        _statusTime = DateTime.Now;
    }

    private async Task FullRedraw()
    {
        var sb = new StringBuilder();
        sb.Append(HideCursor);
        sb.Append(CursorHome);

        // Header
        var title = $" EDIT: {_virtualPath}";
        if (_modified) title += " [Modified]";
        title = title.PadRight(_screenWidth);
        if (title.Length > _screenWidth) title = title[.._screenWidth];
        sb.Append(ReverseVideo).Append(title).Append(ResetAttr).Append("\r\n");

        // Content area
        var lineNumWidth = 5;
        var textWidth = _screenWidth - lineNumWidth;

        for (int screenRow = 0; screenRow < ContentHeight; screenRow++)
        {
            var lineIdx = _viewportTop + screenRow;

            if (lineIdx < _lines.Count)
            {
                var line = _lines[lineIdx].ToString();
                var lineNum = $"{lineIdx + 1,4}:";

                // Apply horizontal scroll
                if (_viewportLeft < line.Length)
                    line = line[_viewportLeft..];
                else
                    line = "";

                if (line.Length > textWidth)
                    line = line[..textWidth];

                // Sanitize to prevent control characters from corrupting display
                line = SanitizeForDisplay(line);

                sb.Append(lineNum).Append(line).Append(ClearLine).Append("\r\n");
            }
            else
            {
                sb.Append("   ~ ").Append(ClearLine).Append("\r\n");
            }
        }

        await WriteRawAsync(sb.ToString());
        await DrawStatusBar();
        await DrawHelpBar();
        await UpdateCursor();
    }

    private async Task RefreshCurrentLine()
    {
        var screenRow = _cursorRow - _viewportTop + HeaderLines;
        if (screenRow < HeaderLines || screenRow > ContentHeight + HeaderLines - 1)
            return;

        var lineNumWidth = 5;
        var textWidth = _screenWidth - lineNumWidth;

        var line = CurrentLine.ToString();
        var lineNum = $"{_cursorRow + 1,4}:";

        if (_viewportLeft < line.Length)
            line = line[_viewportLeft..];
        else
            line = "";

        if (line.Length > textWidth)
            line = line[..textWidth];

        // Sanitize to prevent control characters from corrupting display
        line = SanitizeForDisplay(line);

        await WriteRawAsync($"{Esc}[{screenRow};1H{lineNum}{line}{ClearLine}");
        await UpdateCursor();
    }

    /// <summary>
    /// Sanitize text for terminal display by filtering out control characters
    /// that could interfere with ANSI escape sequences.
    /// </summary>
    private static string SanitizeForDisplay(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // Filter out control characters (0-31) except tab (9)
            // Tab should already be converted to spaces, but just in case
            if (ch >= 32 || ch == '\t')
            {
                sb.Append(ch);
            }
            // Optionally show control chars as visible: sb.Append($"^{(char)(ch + 64)}");
        }
        return sb.ToString();
    }

    private async Task DrawStatusBar()
    {
        var row = _screenHeight - 1;
        var status = _statusMessage.Length > 0
            ? _statusMessage
            : $"Line {_cursorRow + 1}/{_lines.Count}, Col {_cursorCol + 1}";

        status = status.PadRight(_screenWidth);
        if (status.Length > _screenWidth) status = status[.._screenWidth];

        await WriteRawAsync($"{Esc}[{row};1H{ReverseVideo}{status}{ResetAttr}");
    }

    private async Task DrawHelpBar()
    {
        var row = _screenHeight;
        var help = "^G Help  ^O Save  ^X Exit  ^K Cut Line";
        help = help.PadRight(_screenWidth);
        if (help.Length > _screenWidth) help = help[.._screenWidth];

        await WriteRawAsync($"{Esc}[{row};1H{ReverseVideo}{help}{ResetAttr}");
    }

    private async Task UpdateCursor()
    {
        var screenRow = _cursorRow - _viewportTop + HeaderLines;
        var screenCol = _cursorCol - _viewportLeft + 5 + 1; // +5 for line number, +1 for 1-based

        await WriteRawAsync($"{Esc}[{screenRow};{screenCol}H{ShowCursor}");
    }

    private async Task WriteRawAsync(string text)
    {
        if (_session is TelnetSession telnet)
        {
            await telnet.WriteRawAsync(text);
        }
        else
        {
            await _session.WriteAsync(text);
        }
    }
}
