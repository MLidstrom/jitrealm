namespace JitRealm.Mud.Formatting;

/// <summary>
/// Simple scrolling UI for clients without ANSI cursor control support.
/// Just writes lines sequentially with no screen manipulation.
/// </summary>
public sealed class SimpleScrollUI : ITerminalUI
{
    private readonly Func<string, Task> _writeAsync;
    private readonly Func<string, Task> _writeLineAsync;

    public bool SupportsSplitScreen => false;
    public int Width { get; }
    public int Height { get; }

    public SimpleScrollUI(
        Func<string, Task> writeAsync,
        Func<string, Task> writeLineAsync,
        int width = 80,
        int height = 24)
    {
        _writeAsync = writeAsync;
        _writeLineAsync = writeLineAsync;
        Width = width;
        Height = height;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task WriteOutputAsync(string text) => _writeLineAsync(text);

    public async Task WriteOutputLinesAsync(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            await _writeLineAsync(line);
        }
    }

    public Task UpdateStatusBarAsync(StatusBarData data)
    {
        // In simple mode, status is shown on request (score command) rather than persistently
        return Task.CompletedTask;
    }

    public Task RenderInputLineAsync(string prompt, string currentInput = "")
        => _writeAsync(prompt);

    public Task ResetTerminalAsync() => Task.CompletedTask;
}
