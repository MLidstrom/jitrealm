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

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Task.FromResult<string?>(null);

        try
        {
            // Only attempt to read if data is available - prevents blocking
            if (!_stream.DataAvailable)
            {
                return Task.FromResult<string?>(null);
            }

            // Read available data character by character until we get a full line
            while (_stream.DataAvailable && !cancellationToken.IsCancellationRequested)
            {
                var ch = (char)_reader.Read();

                // Handle line endings: \r\n, \n, or \r alone
                if (ch == '\n')
                {
                    var line = _inputBuffer.ToString();
                    _inputBuffer.Clear();
                    return Task.FromResult<string?>(line);
                }
                else if (ch == '\r')
                {
                    // Check if next char is \n
                    if (_stream.DataAvailable)
                    {
                        var peek = (char)_reader.Peek();
                        if (peek == '\n')
                        {
                            _reader.Read(); // consume the \n
                        }
                    }
                    var line = _inputBuffer.ToString();
                    _inputBuffer.Clear();
                    return Task.FromResult<string?>(line);
                }
                else
                {
                    _inputBuffer.Append(ch);
                }
            }

            // Not a complete line yet
            return Task.FromResult<string?>(null);
        }
        catch (IOException)
        {
            // Connection lost
            return Task.FromResult<string?>(null);
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
