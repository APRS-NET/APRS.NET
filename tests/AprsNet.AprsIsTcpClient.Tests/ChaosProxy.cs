using System.Net;
using System.Net.Sockets;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>
/// A TCP proxy on loopback that misbehaves on cue, to test reconnects over real sockets: <see cref="DropAll"/> cuts
/// every connection, <see cref="Stall"/> stops forwarding without closing anything (a link that has silently died),
/// and <see cref="Refusing"/> resets new connections as soon as they arrive.
/// </summary>
/// <remarks>Also compiled into the KISS tests.</remarks>
internal sealed class ChaosProxy : IAsyncDisposable
{
    private readonly IPEndPoint _upstream;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Pair> _pairs = [];
    private readonly Task _accepting;
    private int _accepted;
    private int _refused;

    public ChaosProxy(IPEndPoint upstream)
    {
        _upstream = upstream;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    /// <summary>Where clients connect.</summary>
    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    /// <summary>When set, each new connection is reset at once instead of forwarded.</summary>
    public bool Refusing { get; set; }

    /// <summary>Connections forwarded so far.</summary>
    public int Accepted => Volatile.Read(ref _accepted);

    /// <summary>Connections reset because <see cref="Refusing"/> was set.</summary>
    public int Refused => Volatile.Read(ref _refused);

    /// <summary>Connections being forwarded or stalled now.</summary>
    public int Open
    {
        get
        {
            lock (_pairs)
            {
                return _pairs.Count;
            }
        }
    }

    /// <summary>Cuts every open connection, both sides, with a reset.</summary>
    public void DropAll()
    {
        foreach (var pair in Take())
        {
            pair.Reset();
        }
    }

    /// <summary>
    /// Stops forwarding on every open connection without closing it: neither side hears anything more, as when a link
    /// dies without a reset. New connections are forwarded as usual.
    /// </summary>
    public void Stall()
    {
        lock (_pairs)
        {
            foreach (var pair in _pairs)
            {
                pair.Stalled = true;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        await _accepting;
        DropAll();
        _listener.Dispose();
        _stopping.Dispose();
    }

    private Pair[] Take()
    {
        lock (_pairs)
        {
            var all = _pairs.ToArray();
            _pairs.Clear();
            return all;
        }
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            if (Refusing)
            {
                Interlocked.Increment(ref _refused);
                client.Client.LingerState = new LingerOption(true, 0);
                client.Dispose();
                continue;
            }

            var server = new TcpClient();
            try
            {
                await server.ConnectAsync(_upstream, _stopping.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException)
            {
                server.Dispose();
                client.Client.LingerState = new LingerOption(true, 0);
                client.Dispose();
                continue;
            }

            var pair = new Pair(client, server);
            lock (_pairs)
            {
                _pairs.Add(pair);
            }

            Interlocked.Increment(ref _accepted);
            pair.Start(() =>
            {
                lock (_pairs)
                {
                    _pairs.Remove(pair);
                }
            });
        }
    }

    /// <summary>A client connection and its upstream connection, copied both ways.</summary>
    private sealed class Pair(TcpClient client, TcpClient server)
    {
        private int _closed;

        public volatile bool Stalled;

        public void Start(Action closed)
        {
            _ = Task.WhenAll(CopyAsync(client, server), CopyAsync(server, client))
                .ContinueWith(_ => closed(), TaskScheduler.Default);
        }

        public void Reset()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }

            foreach (var tcp in new[] { client, server })
            {
                try
                {
                    tcp.Client.LingerState = new LingerOption(true, 0);
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException)
                {
                    // Already closed.
                }

                tcp.Dispose();
            }
        }

        private async Task CopyAsync(TcpClient from, TcpClient to)
        {
            var buffer = new byte[4096];
            try
            {
                while (true)
                {
                    var read = await from.GetStream().ReadAsync(buffer);
                    if (read == 0)
                    {
                        break;
                    }

                    // A stalled link swallows what it reads, so neither side sees an error or a close.
                    if (!Stalled)
                    {
                        await to.GetStream().WriteAsync(buffer.AsMemory(0, read));
                    }
                }
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                // One side went away.
            }

            // A clean close on one side is passed on, unless the link is stalled.
            if (!Stalled)
            {
                Reset();
            }
        }
    }
}
