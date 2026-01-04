using JitRealm.Mud.Formatting;

namespace JitRealm.Mud.Network;

/// <summary>
/// Session implementation for local console (single-player mode).
/// </summary>
public sealed class ConsoleSession : ISession
{
    private bool _supportsAnsi = true;
    private IMudFormatter? _formatter;

    public string SessionId { get; } = "console";
    public string? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public bool IsWizard { get; set; } = true; // Console user is always a wizard
    public bool IsConnected => true; // Console is always "connected"
    public bool HasPendingInput => Console.KeyAvailable;

    public bool SupportsAnsi
    {
        get => _supportsAnsi;
        set
        {
            _supportsAnsi = value;
            _formatter = null; // Force re-creation
        }
    }

    public IMudFormatter Formatter =>
        _formatter ??= _supportsAnsi ? new MudFormatter() : new PlainTextFormatter();

    // Console mode does not use split-screen UI
    public ITerminalUI? TerminalUI => null;
    public bool SupportsSplitScreen => false;
    public (int Width, int Height) TerminalSize { get; set; } = (80, 24);

    public Task WriteLineAsync(string text)
    {
        Console.WriteLine(text);
        return Task.CompletedTask;
    }

    public Task WriteAsync(string text)
    {
        Console.Write(text);
        return Task.CompletedTask;
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        // Console.ReadLine is blocking, but for console mode that's fine
        var line = Console.ReadLine();
        return Task.FromResult(line);
    }

    public Task<char?> ReadCharAsync(CancellationToken cancellationToken = default)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            return Task.FromResult<char?>(key.KeyChar);
        }
        return Task.FromResult<char?>(null);
    }

    public Task CloseAsync()
    {
        // Nothing to close for console
        return Task.CompletedTask;
    }
}
