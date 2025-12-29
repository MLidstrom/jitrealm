using System.Net;
using System.Net.Sockets;

namespace JitRealm.Mud.Network;

/// <summary>
/// TCP server that accepts telnet connections.
/// </summary>
public sealed class TelnetServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly int _port;
    private int _sessionCounter;
    private bool _running;
    private bool _disposed;

    public event Action<TelnetSession>? OnClientConnected;

    public TelnetServer(int port = 4000)
    {
        _port = port;
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public int Port => _port;

    public void Start()
    {
        if (_running) return;

        _listener.Start();
        _running = true;
    }

    public void Stop()
    {
        if (!_running) return;

        _running = false;
        _listener.Stop();
    }

    /// <summary>
    /// Check for and accept any pending connections.
    /// Call this periodically from the game loop.
    /// </summary>
    public async Task AcceptPendingConnectionsAsync()
    {
        if (!_running) return;

        while (_listener.Pending())
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                var sessionId = $"telnet-{Interlocked.Increment(ref _sessionCounter):D6}";
                var session = new TelnetSession(client, sessionId);

                OnClientConnected?.Invoke(session);
            }
            catch (SocketException)
            {
                // Listener was stopped
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
    }
}
