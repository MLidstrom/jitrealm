using System.Net.Sockets;
using System.Text;

namespace JitRealm.Mud.Network;

/// <summary>
/// Session implementation for telnet TCP connections.
/// </summary>
public sealed class TelnetSession : ISession, IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly StringBuilder _inputBuffer = new();
    private bool _disposed;

    public string SessionId { get; }
    public string? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public bool IsWizard { get; set; }
    public bool IsConnected => !_disposed && _client.Connected;
    public bool HasPendingInput => _stream.DataAvailable;

    public TelnetSession(TcpClient client, string sessionId)
    {
        _client = client;
        SessionId = sessionId;

        _stream = client.GetStream();
        _reader = new StreamReader(_stream, Encoding.ASCII);
        _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };
    }

    public async Task WriteLineAsync(string text)
    {
        if (!IsConnected) return;

        try
        {
            // Telnet uses \r\n for line endings
            await _writer.WriteAsync(text + "\r\n");
        }
        catch (IOException)
        {
            // Connection lost
        }
    }

    public async Task WriteAsync(string text)
    {
        if (!IsConnected) return;

        try
        {
            await _writer.WriteAsync(text);
        }
        catch (IOException)
        {
            // Connection lost
        }
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        try
        {
            // Use a timeout to allow checking for cancellation
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));

            var line = await _reader.ReadLineAsync(cts.Token);
            return line;
        }
        catch (OperationCanceledException)
        {
            // Timeout or cancellation - no input available
            return null;
        }
        catch (IOException)
        {
            // Connection lost
            return null;
        }
    }

    public async Task CloseAsync()
    {
        if (_disposed) return;

        try
        {
            await WriteLineAsync("Goodbye!");
        }
        catch
        {
            // Ignore errors during close
        }

        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _reader.Dispose();
        _writer.Dispose();
        _stream.Dispose();
        _client.Dispose();
    }
}
