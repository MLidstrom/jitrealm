namespace JitRealm.Mud.Formatting;

/// <summary>
/// Interface for terminal UI rendering modes.
/// Implementations handle screen layout and cursor management.
/// </summary>
public interface ITerminalUI
{
    /// <summary>
    /// Whether this UI mode supports split-screen rendering with fixed regions.
    /// </summary>
    bool SupportsSplitScreen { get; }

    /// <summary>
    /// Terminal width in columns.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Terminal height in rows.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Initialize the terminal UI (clear screen, set scroll regions, etc.)
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Write a line to the output area (scrolling region).
    /// </summary>
    Task WriteOutputAsync(string text);

    /// <summary>
    /// Write multiple lines to the output area.
    /// </summary>
    Task WriteOutputLinesAsync(IEnumerable<string> lines);

    /// <summary>
    /// Update the status bar with current player state.
    /// </summary>
    Task UpdateStatusBarAsync(StatusBarData data);

    /// <summary>
    /// Render the input line with prompt and current input buffer.
    /// </summary>
    Task RenderInputLineAsync(string prompt, string currentInput = "");

    /// <summary>
    /// Reset terminal to normal mode (clear scroll regions, show cursor, etc.)
    /// Called on disconnect.
    /// </summary>
    Task ResetTerminalAsync();
}
