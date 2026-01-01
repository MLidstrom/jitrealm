using System.Net.Sockets;
using System.Text;
using JitRealm.Mud.Formatting;

namespace JitRealm.Mud.Network;

/// <summary>
/// Session implementation for telnet TCP connections.
/// </summary>
public sealed class TelnetSession : ISession, IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly StreamWriter _writer;
    private readonly StringBuilder _currentLine = new();
    private readonly Queue<string> _completedLines = new();
    private readonly byte[] _readBuffer = new byte[4096];
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly char[] _charBuffer = new char[4096];
    private bool _sawCarriageReturn;
    private int _telnetState; // 0=normal, 1=IAC, 2=IAC verb, 3=SB (subnegotiation)
    private byte _telnetVerb;
    private bool _disposed;
    private bool _supportsAnsi = true;
    private IMudFormatter? _formatter;

    public string SessionId { get; }
    public string? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public bool IsWizard { get; set; }
    public bool IsConnected => !_disposed && _client.Connected;
    public bool HasPendingInput => _completedLines.Count > 0 || (_client.Connected && _client.Available > 0);

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

    public TelnetSession(TcpClient client, string sessionId)
    {
        _client = client;
        SessionId = sessionId;

        _stream = client.GetStream();
        // Use UTF-8 so Unicode box drawing (used by Spectre.Console tables/panels) renders correctly.
        // Pure ANSI escape sequences are ASCII-compatible, so this is safe for color codes as well.
        _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };
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

    /// <summary>
    /// Non-blocking read - returns null if no complete line available.
    /// Used by the game loop for polling input.
    /// </summary>
    public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Task.FromResult<string?>(null);

        try
        {
            PumpAvailableBytes();

            if (_completedLines.Count > 0)
                return Task.FromResult<string?>(_completedLines.Dequeue());

            return Task.FromResult<string?>(null);
        }
        catch (IOException)
        {
            // Connection lost
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// Blocking read - waits for a complete line of input.
    /// Used by login/registration flow.
    /// </summary>
    public async Task<string?> ReadLineBlockingAsync(CancellationToken cancellationToken = default)
    {
        while (IsConnected && !cancellationToken.IsCancellationRequested)
        {
            var line = await ReadLineAsync(cancellationToken);
            if (line is not null)
                return line;

            // Light backoff: avoids spinning while still feeling responsive for login prompts.
            await Task.Delay(10, cancellationToken);
        }

        return null;
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

        _writer.Dispose();
        _stream.Dispose();
        _client.Dispose();
    }

    private void PumpAvailableBytes()
    {
        // IMPORTANT:
        // Do NOT use NetworkStream.DataAvailable together with StreamReader buffering.
        // We read raw bytes based on TcpClient.Available to avoid the "one command behind" bug.
        while (IsConnected && _client.Available > 0)
        {
            var toRead = Math.Min(_client.Available, _readBuffer.Length);
            var bytesRead = _stream.Read(_readBuffer, 0, toRead);
            if (bytesRead <= 0)
                return;

            // Filter telnet negotiation bytes (IAC sequences) before decoding.
            var filtered = new byte[bytesRead];
            var filteredLen = 0;

            for (var i = 0; i < bytesRead; i++)
            {
                var b = _readBuffer[i];

                switch (_telnetState)
                {
                    case 0: // normal
                        if (b == 255) // IAC
                        {
                            _telnetState = 1;
                            continue;
                        }
                        filtered[filteredLen++] = b;
                        break;

                    case 1: // after IAC
                        // Escaped 255 (IAC IAC) => literal 255 in stream
                        if (b == 255)
                        {
                            _telnetState = 0;
                            filtered[filteredLen++] = 255;
                            break;
                        }

                        // Subnegotiation start: IAC SB
                        if (b == 250)
                        {
                            _telnetState = 3;
                            break;
                        }

                        // Negotiation verbs that take an option byte next: WILL/WONT/DO/DONT
                        if (b is 251 or 252 or 253 or 254)
                        {
                            _telnetVerb = b;
                            _telnetState = 2;
                            break;
                        }

                        // Anything else: ignore and return to normal
                        _telnetState = 0;
                        break;

                    case 2: // expecting option byte after WILL/WONT/DO/DONT
                        _telnetState = 0;
                        break;

                    case 3: // subnegotiation: ignore until IAC SE
                        if (b == 255)
                        {
                            _telnetState = 1; // re-use "after IAC" handling (SE=240 will drop us back)
                        }
                        break;
                }
            }

            if (filteredLen == 0)
                continue;

            var charsDecoded = _decoder.GetChars(filtered, 0, filteredLen, _charBuffer, 0, flush: false);
            for (var c = 0; c < charsDecoded; c++)
            {
                var ch = _charBuffer[c];

                // Handle CRLF, LF, or CR.
                if (ch == '\r')
                {
                    _sawCarriageReturn = true;
                    CompleteLine();
                    continue;
                }

                if (ch == '\n')
                {
                    if (_sawCarriageReturn)
                    {
                        _sawCarriageReturn = false;
                        continue; // swallow LF after CR
                    }
                    CompleteLine();
                    continue;
                }

                // Some telnet clients send CR NUL; ignore NUL right after CR.
                if (_sawCarriageReturn && ch == '\0')
                {
                    _sawCarriageReturn = false;
                    continue;
                }
                _sawCarriageReturn = false;

                // Backspace handling (BS or DEL)
                if (ch == '\b' || ch == (char)127)
                {
                    if (_currentLine.Length > 0)
                        _currentLine.Length -= 1;
                    continue;
                }

                _currentLine.Append(ch);
            }
        }
    }

    private void CompleteLine()
    {
        var line = _currentLine.ToString();
        _currentLine.Clear();
        _completedLines.Enqueue(line);
    }
}
