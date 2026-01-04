using System.Net.Sockets;
using System.Text;
using JitRealm.Mud.Formatting;

namespace JitRealm.Mud.Network;

/// <summary>
/// Session implementation for telnet TCP connections.
/// </summary>
public sealed class TelnetSession : ISession, IDisposable
{
    // Telnet protocol bytes
    private const byte Iac = 255;
    private const byte Will = 251;
    private const byte Wont = 252;
    private const byte Do = 253;
    private const byte Dont = 254;
    private const byte Sb = 250;
    private const byte Se = 240;
    private const byte Naws = 31;  // Negotiate About Window Size
    private const byte Echo = 1;   // Echo option
    private const byte Sga = 3;    // Suppress Go Ahead

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly StreamWriter _writer;
    private readonly StringBuilder _currentLine = new();
    private readonly Queue<string> _completedLines = new();
    private readonly Queue<char> _pendingChars = new();
    private readonly byte[] _readBuffer = new byte[4096];
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly char[] _charBuffer = new char[4096];
    private bool _sawCarriageReturn;
    // Telnet parsing state:
    // 0=normal, 1=after IAC, 2=after IAC (WILL/WONT/DO/DONT) expecting option,
    // 3=subnegotiation data, 4=subnegotiation expecting option, 5=subnegotiation after IAC.
    private int _telnetState;
    private byte _telnetVerb;
    private byte _sbOption;
    private readonly byte[] _sbBuffer = new byte[64];
    private int _sbLen;
    private bool _disposed;
    private bool _supportsAnsi = true;
    private bool _characterMode;  // True when in character-at-a-time mode (for editors)
    private bool _lineEditMode;   // True when using line editor with history
    private readonly LineEditor _lineEditor = new();
    private IMudFormatter? _formatter;
    private ITerminalUI? _terminalUI;
    private bool _splitScreenEnabled;
    private (int Width, int Height) _terminalSize = (80, 24);

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

    public ITerminalUI? TerminalUI => _terminalUI;

    public bool SupportsSplitScreen => _splitScreenEnabled && _supportsAnsi;

    /// <summary>
    /// Whether line edit mode (with command history) is currently enabled.
    /// </summary>
    public bool IsLineEditModeEnabled => _lineEditMode;

    public (int Width, int Height) TerminalSize
    {
        get => _terminalSize;
        set => _terminalSize = value;
    }

    public TelnetSession(TcpClient client, string sessionId)
    {
        _client = client;
        SessionId = sessionId;

        _stream = client.GetStream();
        // Use UTF-8 so Unicode box drawing (used by Spectre.Console tables/panels) renders correctly.
        // Pure ANSI escape sequences are ASCII-compatible, so this is safe for color codes as well.
        _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

        // Request NAWS so we can react to terminal resizes.
        // Many clients will respond WILL NAWS + SB NAWS <w><h>.
        SendTelnetCommand(Do, Naws);
    }

    /// <summary>
    /// Enable split-screen terminal UI with fixed status bar and input line.
    /// </summary>
    public async Task EnableSplitScreenAsync()
    {
        if (!_supportsAnsi) return;

        _splitScreenEnabled = true;
        _terminalUI = new SplitScreenUI(
            WriteRawAsync,
            Formatter,
            _terminalSize.Width,
            _terminalSize.Height
        );

        await _terminalUI.InitializeAsync();
    }

    /// <summary>
    /// Disable split-screen UI and return to simple scrolling mode.
    /// </summary>
    public async Task DisableSplitScreenAsync()
    {
        if (_terminalUI != null)
        {
            await _terminalUI.ResetTerminalAsync();
        }

        _splitScreenEnabled = false;
        _terminalUI = null;
    }

    /// <summary>
    /// Enable character-at-a-time mode for interactive editors.
    /// This negotiates telnet options to disable local echo and line buffering.
    /// </summary>
    public void EnableCharacterMode()
    {
        if (_characterMode) return;
        _characterMode = true;

        // WILL ECHO: Server will echo, client should not
        SendTelnetCommand(Will, Echo);
        // WILL SGA: Suppress go-ahead (full duplex mode)
        SendTelnetCommand(Will, Sga);
        // DO SGA: Ask client to also suppress go-ahead
        SendTelnetCommand(Do, Sga);
    }

    /// <summary>
    /// Disable character-at-a-time mode and return to line mode.
    /// </summary>
    public void DisableCharacterMode()
    {
        if (!_characterMode) return;
        _characterMode = false;

        // WONT ECHO: Server won't echo, client can echo locally
        SendTelnetCommand(Wont, Echo);
        // Note: We keep SGA enabled as it doesn't hurt normal operation
    }

    /// <summary>
    /// Enable line edit mode with command history.
    /// This enables character mode and uses the LineEditor for input processing.
    /// </summary>
    public void EnableLineEditMode()
    {
        if (_lineEditMode) return;
        _lineEditMode = true;
        EnableCharacterMode();
    }

    /// <summary>
    /// Disable line edit mode and return to client-side line editing.
    /// </summary>
    public void DisableLineEditMode()
    {
        if (!_lineEditMode) return;
        _lineEditMode = false;
        _lineEditor.Reset();
        DisableCharacterMode();
    }

    /// <summary>
    /// Write raw text directly to the stream, bypassing terminal UI routing.
    /// Used by the terminal UI itself and for ANSI sequences.
    /// </summary>
    public async Task WriteRawAsync(string text)
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

    public async Task WriteLineAsync(string text)
    {
        if (!IsConnected) return;

        try
        {
            if (_terminalUI?.SupportsSplitScreen == true)
            {
                // Route through split-screen UI (handles scroll region)
                await _terminalUI.WriteOutputAsync(text);
            }
            else
            {
                // Original behavior: telnet uses \r\n for line endings
                await _writer.WriteAsync(text + "\r\n");
            }
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

    /// <summary>
    /// Non-blocking read of a single character.
    /// Used by interactive editors for character-by-character input.
    /// </summary>
    public Task<char?> ReadCharAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return Task.FromResult<char?>(null);

        try
        {
            PumpAvailableChars();

            if (_pendingChars.Count > 0)
                return Task.FromResult<char?>(_pendingChars.Dequeue());

            return Task.FromResult<char?>(null);
        }
        catch (IOException)
        {
            return Task.FromResult<char?>(null);
        }
    }

    public async Task CloseAsync()
    {
        if (_disposed) return;

        try
        {
            // Reset terminal to normal mode if split-screen was active
            if (_terminalUI != null)
            {
                await _terminalUI.ResetTerminalAsync();
            }

            await WriteRawAsync("Goodbye!\r\n");
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
                        if (b == Iac) // IAC
                        {
                            _telnetState = 1;
                            continue;
                        }
                        filtered[filteredLen++] = b;
                        break;

                    case 1: // after IAC
                        // Escaped 255 (IAC IAC) => literal 255 in stream
                        if (b == Iac)
                        {
                            _telnetState = 0;
                            filtered[filteredLen++] = 255;
                            break;
                        }

                        // Subnegotiation start: IAC SB
                        if (b == Sb)
                        {
                            _telnetState = 4; // expecting subnegotiation option
                            _sbLen = 0;
                            break;
                        }

                        // Negotiation verbs that take an option byte next: WILL/WONT/DO/DONT
                        if (b is Will or Wont or Do or Dont)
                        {
                            _telnetVerb = b;
                            _telnetState = 2;
                            break;
                        }

                        // Anything else: ignore and return to normal
                        _telnetState = 0;
                        break;

                    case 2: // expecting option byte after WILL/WONT/DO/DONT
                        HandleTelnetNegotiation(_telnetVerb, b);
                        _telnetState = 0;
                        break;

                    case 4: // subnegotiation: expecting option byte
                        _sbOption = b;
                        _telnetState = 3;
                        break;

                    case 3: // subnegotiation: read until IAC SE
                        if (b == Iac)
                        {
                            _telnetState = 5;
                            break;
                        }
                        if (_sbLen < _sbBuffer.Length)
                            _sbBuffer[_sbLen++] = b;
                        break;

                    case 5: // subnegotiation: after IAC
                        if (b == Se)
                        {
                            HandleSubnegotiation(_sbOption, _sbBuffer, _sbLen);
                            _telnetState = 0;
                            break;
                        }
                        if (b == Iac)
                        {
                            // Escaped IAC within SB
                            if (_sbLen < _sbBuffer.Length)
                                _sbBuffer[_sbLen++] = Iac;
                            _telnetState = 3;
                            break;
                        }
                        // Unknown command within SB, continue consuming data.
                        _telnetState = 3;
                        break;
                }
            }

            if (filteredLen == 0)
                continue;

            var charsDecoded = _decoder.GetChars(filtered, 0, filteredLen, _charBuffer, 0, flush: false);
            for (var c = 0; c < charsDecoded; c++)
            {
                var ch = _charBuffer[c];

                // Line edit mode: route all characters through the LineEditor
                if (_lineEditMode)
                {
                    ProcessLineEditChar(ch);
                    continue;
                }

                // Normal mode: simple line accumulation

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

    /// <summary>
    /// Process a character through the line editor (when in line edit mode).
    /// </summary>
    private void ProcessLineEditChar(char ch)
    {
        // Handle CR/LF: convert to single newline for the editor
        if (ch == '\r')
        {
            _sawCarriageReturn = true;
            var result = _lineEditor.ProcessChar('\n');
            HandleLineEditResult(result);
            return;
        }

        if (ch == '\n')
        {
            if (_sawCarriageReturn)
            {
                _sawCarriageReturn = false;
                return; // swallow LF after CR
            }
            var result = _lineEditor.ProcessChar('\n');
            HandleLineEditResult(result);
            return;
        }

        // CR NUL handling
        if (_sawCarriageReturn && ch == '\0')
        {
            _sawCarriageReturn = false;
            return;
        }
        _sawCarriageReturn = false;

        // Process all other characters through the editor
        var editResult = _lineEditor.ProcessChar(ch);
        HandleLineEditResult(editResult);
    }

    private void HandleLineEditResult(LineEditResult result)
    {
        // Echo feedback to the terminal
        if (!string.IsNullOrEmpty(result.Echo))
        {
            try
            {
                _writer.Write(result.Echo);
            }
            catch (IOException)
            {
                // Connection lost
            }
        }

        // If a line was completed, queue it
        if (result.CompletedLine is not null)
        {
            _completedLines.Enqueue(result.CompletedLine);
        }
    }

    private void CompleteLine()
    {
        var line = _currentLine.ToString();
        _currentLine.Clear();
        _completedLines.Enqueue(line);
    }

    /// <summary>
    /// Pump available bytes into the character queue (for ReadCharAsync).
    /// Similar to PumpAvailableBytes but does not build lines.
    /// </summary>
    private void PumpAvailableChars()
    {
        while (IsConnected && _client.Available > 0)
        {
            var toRead = Math.Min(_client.Available, _readBuffer.Length);
            var bytesRead = _stream.Read(_readBuffer, 0, toRead);
            if (bytesRead <= 0)
                return;

            // Filter telnet negotiation bytes
            var filtered = new byte[bytesRead];
            var filteredLen = 0;

            for (var i = 0; i < bytesRead; i++)
            {
                var b = _readBuffer[i];

                switch (_telnetState)
                {
                    case 0: // normal
                        if (b == Iac)
                        {
                            _telnetState = 1;
                            continue;
                        }
                        filtered[filteredLen++] = b;
                        break;

                    case 1: // after IAC
                        if (b == Iac)
                        {
                            _telnetState = 0;
                            filtered[filteredLen++] = 255;
                            break;
                        }
                        if (b == Sb)
                        {
                            _telnetState = 4;
                            _sbLen = 0;
                            break;
                        }
                        if (b is Will or Wont or Do or Dont)
                        {
                            _telnetVerb = b;
                            _telnetState = 2;
                            break;
                        }
                        _telnetState = 0;
                        break;

                    case 2: // expecting option byte
                        HandleTelnetNegotiation(_telnetVerb, b);
                        _telnetState = 0;
                        break;

                    case 4: // subnegotiation: expecting option
                        _sbOption = b;
                        _telnetState = 3;
                        break;

                    case 3: // subnegotiation data
                        if (b == Iac)
                        {
                            _telnetState = 5;
                            break;
                        }
                        if (_sbLen < _sbBuffer.Length)
                            _sbBuffer[_sbLen++] = b;
                        break;

                    case 5: // subnegotiation: after IAC
                        if (b == Se)
                        {
                            HandleSubnegotiation(_sbOption, _sbBuffer, _sbLen);
                            _telnetState = 0;
                            break;
                        }
                        if (b == Iac)
                        {
                            if (_sbLen < _sbBuffer.Length)
                                _sbBuffer[_sbLen++] = Iac;
                            _telnetState = 3;
                            break;
                        }
                        _telnetState = 3;
                        break;
                }
            }

            if (filteredLen == 0)
                continue;

            var charsDecoded = _decoder.GetChars(filtered, 0, filteredLen, _charBuffer, 0, flush: false);
            for (var c = 0; c < charsDecoded; c++)
            {
                _pendingChars.Enqueue(_charBuffer[c]);
            }
        }
    }

    private void SendTelnetCommand(byte verb, byte option)
    {
        if (!IsConnected) return;

        try
        {
            // Avoid interleaving with UTF-8 output as much as possible.
            _writer.Flush();
            _stream.Write(new[] { Iac, verb, option }, 0, 3);
        }
        catch
        {
            // Ignore negotiation failures; client may not support it.
        }
    }

    private void HandleTelnetNegotiation(byte verb, byte option)
    {
        // We only care about NAWS right now.
        if (verb == Will && option == Naws)
        {
            // Client says it WILL send window size -> acknowledge.
            SendTelnetCommand(Do, Naws);
        }
        else if (verb == Wont && option == Naws)
        {
            // Client refuses. Keep default size.
        }
        else if (verb == Do && option == Naws)
        {
            // Client asks us to perform NAWS (not applicable for server); refuse.
            SendTelnetCommand(Wont, Naws);
        }
    }

    private void HandleSubnegotiation(byte option, byte[] buffer, int len)
    {
        if (option != Naws)
            return;

        if (len < 4)
            return;

        var width = (buffer[0] << 8) | buffer[1];
        var height = (buffer[2] << 8) | buffer[3];

        // Some clients can send zeros during startup; ignore those.
        if (width <= 0 || height <= 0)
            return;

        // Clamp to sane minimums to keep UI stable.
        width = Math.Max(40, width);
        height = Math.Max(10, height);

        if (_terminalSize.Width == width && _terminalSize.Height == height)
            return;

        _terminalSize = (width, height);

        // If split-screen is active, resize the UI immediately so the status bar fills the new width.
        if (_terminalUI is SplitScreenUI ui && _splitScreenEnabled && _supportsAnsi)
        {
            _ = ui.ResizeAsync(width, height);
        }
    }
}
