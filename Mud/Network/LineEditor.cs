using System.Text;

namespace JitRealm.Mud.Network;

/// <summary>
/// Line editor with command history support for telnet sessions.
/// Handles character-by-character input, arrow key navigation, and history.
/// </summary>
public sealed class LineEditor
{
    private const int MaxHistorySize = 100;

    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private string _currentEdit = "";

    private readonly StringBuilder _line = new();
    private int _cursorPos;

    // Escape sequence parsing state
    private int _escapeState; // 0=normal, 1=ESC, 2=ESC[

    /// <summary>
    /// Process a character and return the result.
    /// </summary>
    /// <param name="ch">The character to process.</param>
    /// <returns>
    /// Result containing: the completed line (if Enter pressed),
    /// or output to echo back to the terminal, or null for no action.
    /// </returns>
    public LineEditResult ProcessChar(char ch)
    {
        // Handle escape sequences (arrow keys, etc.)
        if (_escapeState > 0)
        {
            return ProcessEscapeSequence(ch);
        }

        if (ch == '\x1b') // ESC
        {
            _escapeState = 1;
            return LineEditResult.NoAction;
        }

        // Control characters
        if (ch == '\r' || ch == '\n')
        {
            var line = _line.ToString();
            if (!string.IsNullOrWhiteSpace(line))
            {
                AddToHistory(line);
            }
            _line.Clear();
            _cursorPos = 0;
            _historyIndex = -1;
            _currentEdit = "";
            return new LineEditResult { CompletedLine = line, Echo = "\r\n" };
        }

        if (ch == '\b' || ch == (char)127) // Backspace
        {
            return HandleBackspace();
        }

        if (ch == '\x01') // Ctrl+A - Home
        {
            return MoveCursorHome();
        }

        if (ch == '\x05') // Ctrl+E - End
        {
            return MoveCursorEnd();
        }

        if (ch == '\x0B') // Ctrl+K - Kill to end of line
        {
            return KillToEndOfLine();
        }

        if (ch == '\x15') // Ctrl+U - Kill entire line
        {
            return KillEntireLine();
        }

        // Ignore other control characters
        if (ch < 32)
        {
            return LineEditResult.NoAction;
        }

        // Regular character - insert at cursor position
        return InsertChar(ch);
    }

    private LineEditResult ProcessEscapeSequence(char ch)
    {
        switch (_escapeState)
        {
            case 1: // After ESC
                if (ch == '[')
                {
                    _escapeState = 2;
                    return LineEditResult.NoAction;
                }
                else if (ch == 'O') // SS3 - some terminals use this for arrow keys
                {
                    _escapeState = 2;
                    return LineEditResult.NoAction;
                }
                _escapeState = 0;
                return LineEditResult.NoAction;

            case 2: // After ESC[
                _escapeState = 0;
                return ch switch
                {
                    'A' => HistoryUp(),      // Up arrow
                    'B' => HistoryDown(),    // Down arrow
                    'C' => MoveCursorRight(), // Right arrow
                    'D' => MoveCursorLeft(),  // Left arrow
                    'H' => MoveCursorHome(),  // Home
                    'F' => MoveCursorEnd(),   // End
                    '3' => LineEditResult.NoAction, // Delete key (needs ~)
                    _ => LineEditResult.NoAction
                };

            default:
                _escapeState = 0;
                return LineEditResult.NoAction;
        }
    }

    private LineEditResult InsertChar(char ch)
    {
        if (_cursorPos == _line.Length)
        {
            // Append at end - simple case
            _line.Append(ch);
            _cursorPos++;
            return new LineEditResult { Echo = ch.ToString() };
        }
        else
        {
            // Insert in middle - need to redraw rest of line
            _line.Insert(_cursorPos, ch);
            _cursorPos++;
            var rest = _line.ToString(_cursorPos - 1, _line.Length - _cursorPos + 1);
            // Write the inserted char + rest of line, then move cursor back
            var moveBack = _line.Length - _cursorPos;
            var echo = rest + (moveBack > 0 ? $"\x1b[{moveBack}D" : "");
            return new LineEditResult { Echo = echo };
        }
    }

    private LineEditResult HandleBackspace()
    {
        if (_cursorPos == 0)
            return LineEditResult.NoAction;

        if (_cursorPos == _line.Length)
        {
            // Delete at end - simple case
            _line.Length--;
            _cursorPos--;
            return new LineEditResult { Echo = "\b \b" };
        }
        else
        {
            // Delete in middle - need to redraw rest of line
            _line.Remove(_cursorPos - 1, 1);
            _cursorPos--;
            var rest = _line.ToString(_cursorPos, _line.Length - _cursorPos);
            // Move back, write rest + space, then move cursor back
            var echo = $"\b{rest} \x1b[{rest.Length + 1}D";
            return new LineEditResult { Echo = echo };
        }
    }

    private LineEditResult MoveCursorLeft()
    {
        if (_cursorPos > 0)
        {
            _cursorPos--;
            return new LineEditResult { Echo = "\x1b[D" };
        }
        return LineEditResult.NoAction;
    }

    private LineEditResult MoveCursorRight()
    {
        if (_cursorPos < _line.Length)
        {
            _cursorPos++;
            return new LineEditResult { Echo = "\x1b[C" };
        }
        return LineEditResult.NoAction;
    }

    private LineEditResult MoveCursorHome()
    {
        if (_cursorPos > 0)
        {
            var moves = _cursorPos;
            _cursorPos = 0;
            return new LineEditResult { Echo = $"\x1b[{moves}D" };
        }
        return LineEditResult.NoAction;
    }

    private LineEditResult MoveCursorEnd()
    {
        if (_cursorPos < _line.Length)
        {
            var moves = _line.Length - _cursorPos;
            _cursorPos = _line.Length;
            return new LineEditResult { Echo = $"\x1b[{moves}C" };
        }
        return LineEditResult.NoAction;
    }

    private LineEditResult KillToEndOfLine()
    {
        if (_cursorPos < _line.Length)
        {
            var killed = _line.Length - _cursorPos;
            _line.Length = _cursorPos;
            // Clear from cursor to end of line
            return new LineEditResult { Echo = "\x1b[K" };
        }
        return LineEditResult.NoAction;
    }

    private LineEditResult KillEntireLine()
    {
        if (_line.Length == 0)
            return LineEditResult.NoAction;

        // Move to start, clear line
        var moveBack = _cursorPos > 0 ? $"\x1b[{_cursorPos}D" : "";
        _line.Clear();
        _cursorPos = 0;
        return new LineEditResult { Echo = moveBack + "\x1b[K" };
    }

    private LineEditResult HistoryUp()
    {
        if (_history.Count == 0)
            return LineEditResult.NoAction;

        // Save current edit if at bottom of history
        if (_historyIndex == -1)
        {
            _currentEdit = _line.ToString();
        }

        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            return SetLineContent(_history[_history.Count - 1 - _historyIndex]);
        }

        return LineEditResult.NoAction;
    }

    private LineEditResult HistoryDown()
    {
        if (_historyIndex <= 0)
        {
            if (_historyIndex == 0)
            {
                _historyIndex = -1;
                return SetLineContent(_currentEdit);
            }
            return LineEditResult.NoAction;
        }

        _historyIndex--;
        return SetLineContent(_history[_history.Count - 1 - _historyIndex]);
    }

    private LineEditResult SetLineContent(string content)
    {
        var sb = new StringBuilder();

        // Move to start of line
        if (_cursorPos > 0)
            sb.Append($"\x1b[{_cursorPos}D");

        // Clear line
        sb.Append("\x1b[K");

        // Write new content
        sb.Append(content);

        _line.Clear();
        _line.Append(content);
        _cursorPos = content.Length;

        return new LineEditResult { Echo = sb.ToString() };
    }

    private void AddToHistory(string line)
    {
        // Don't add duplicates of the last entry
        if (_history.Count > 0 && _history[^1] == line)
            return;

        _history.Add(line);

        // Trim history if too large
        if (_history.Count > MaxHistorySize)
        {
            _history.RemoveAt(0);
        }
    }

    /// <summary>
    /// Get the current line content (for display purposes).
    /// </summary>
    public string CurrentLine => _line.ToString();

    /// <summary>
    /// Reset the editor state (but keep history).
    /// </summary>
    public void Reset()
    {
        _line.Clear();
        _cursorPos = 0;
        _historyIndex = -1;
        _currentEdit = "";
        _escapeState = 0;
    }
}

/// <summary>
/// Result of processing a character in the line editor.
/// </summary>
public struct LineEditResult
{
    /// <summary>
    /// If non-null, a complete line was entered (user pressed Enter).
    /// </summary>
    public string? CompletedLine { get; init; }

    /// <summary>
    /// Text to echo back to the terminal for visual feedback.
    /// </summary>
    public string? Echo { get; init; }

    /// <summary>
    /// A result indicating no action (e.g., ignored key).
    /// </summary>
    public static readonly LineEditResult NoAction = new();
}
