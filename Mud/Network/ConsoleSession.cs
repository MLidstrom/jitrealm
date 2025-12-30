namespace JitRealm.Mud.Network;

/// <summary>
/// Session implementation for local console (single-player mode).
/// </summary>
public sealed class ConsoleSession : ISession
{
    public string SessionId { get; } = "console";
    public string? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public bool IsConnected => true; // Console is always "connected"
    public bool HasPendingInput => Console.KeyAvailable;

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

    public Task CloseAsync()
    {
        // Nothing to close for console
        return Task.CompletedTask;
    }
}
