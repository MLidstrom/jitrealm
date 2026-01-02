namespace JitRealm.Mud.Formatting;

/// <summary>
/// VT100/ANSI escape sequences for terminal control.
/// These are widely supported by telnet clients (PuTTY, Windows telnet, Linux telnet).
/// </summary>
public static class AnsiSequences
{
    private const char ESC = '\u001b';

    // Telnet-safe line ending
    public const string CRLF = "\r\n";

    // Screen control
    public static readonly string ClearScreen = $"{ESC}[2J";
    public static readonly string ClearLine = $"{ESC}[2K";
    public static readonly string ClearToEndOfLine = $"{ESC}[K";
    public static readonly string ClearToEndOfScreen = $"{ESC}[J";

    // Cursor positioning
    public static readonly string CursorHome = $"{ESC}[H";
    public static readonly string SaveCursor = $"{ESC}[s";
    public static readonly string RestoreCursor = $"{ESC}[u";

    /// <summary>
    /// Move cursor to specific row and column (1-based).
    /// </summary>
    public static string CursorTo(int row, int col) => $"{ESC}[{row};{col}H";

    /// <summary>
    /// Move cursor up N lines.
    /// </summary>
    public static string CursorUp(int n = 1) => $"{ESC}[{n}A";

    /// <summary>
    /// Move cursor down N lines.
    /// </summary>
    public static string CursorDown(int n = 1) => $"{ESC}[{n}B";

    /// <summary>
    /// Move cursor forward (right) N columns.
    /// </summary>
    public static string CursorForward(int n = 1) => $"{ESC}[{n}C";

    /// <summary>
    /// Move cursor back (left) N columns.
    /// </summary>
    public static string CursorBack(int n = 1) => $"{ESC}[{n}D";

    // Scroll regions (key for split-screen UI)
    /// <summary>
    /// Set scrolling region to lines top through bottom (1-based, inclusive).
    /// Lines outside this region will not scroll.
    /// </summary>
    public static string SetScrollRegion(int top, int bottom) => $"{ESC}[{top};{bottom}r";

    /// <summary>
    /// Reset scroll region to full screen.
    /// </summary>
    public static readonly string ResetScrollRegion = $"{ESC}[r";

    /// <summary>
    /// Scroll content up N lines within scroll region.
    /// </summary>
    public static string ScrollUp(int n = 1) => $"{ESC}[{n}S";

    /// <summary>
    /// Scroll content down N lines within scroll region.
    /// </summary>
    public static string ScrollDown(int n = 1) => $"{ESC}[{n}T";

    // Cursor visibility
    public static readonly string HideCursor = $"{ESC}[?25l";
    public static readonly string ShowCursor = $"{ESC}[?25h";

    // Text attributes
    public static readonly string Reset = $"{ESC}[0m";
    public static readonly string Bold = $"{ESC}[1m";
    public static readonly string Dim = $"{ESC}[2m";
    public static readonly string Underline = $"{ESC}[4m";
    public static readonly string Reverse = $"{ESC}[7m";

    // Basic foreground colors
    public static readonly string FgBlack = $"{ESC}[30m";
    public static readonly string FgRed = $"{ESC}[31m";
    public static readonly string FgGreen = $"{ESC}[32m";
    public static readonly string FgYellow = $"{ESC}[33m";
    public static readonly string FgBlue = $"{ESC}[34m";
    public static readonly string FgMagenta = $"{ESC}[35m";
    public static readonly string FgCyan = $"{ESC}[36m";
    public static readonly string FgWhite = $"{ESC}[37m";

    // Bright foreground colors
    public static readonly string FgBrightBlack = $"{ESC}[90m";
    public static readonly string FgBrightRed = $"{ESC}[91m";
    public static readonly string FgBrightGreen = $"{ESC}[92m";
    public static readonly string FgBrightYellow = $"{ESC}[93m";
    public static readonly string FgBrightBlue = $"{ESC}[94m";
    public static readonly string FgBrightMagenta = $"{ESC}[95m";
    public static readonly string FgBrightCyan = $"{ESC}[96m";
    public static readonly string FgBrightWhite = $"{ESC}[97m";

    // Basic background colors
    public static readonly string BgBlack = $"{ESC}[40m";
    public static readonly string BgRed = $"{ESC}[41m";
    public static readonly string BgGreen = $"{ESC}[42m";
    public static readonly string BgYellow = $"{ESC}[43m";
    public static readonly string BgBlue = $"{ESC}[44m";
    public static readonly string BgMagenta = $"{ESC}[45m";
    public static readonly string BgCyan = $"{ESC}[46m";
    public static readonly string BgWhite = $"{ESC}[47m";
}
