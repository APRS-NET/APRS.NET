using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AprsNet.Packets;

namespace AprsNet.Gateway;

/// <summary>
/// A local APRS-IS server. APRS-IS clients connect and log in as they would to a public server
/// (<c>user CALL pass NNNNN vers NAME VERSION filter …</c>), and each receives the packets given to
/// <see cref="Publish"/> that its filter passes, plus any message addressed to its callsign.
/// </summary>
/// <remarks>
/// <para>
/// Filters use <see cref="Filters.AprsFilter"/>, set at login or later with <c>#filter</c>. Logins are verified
/// against the APRS-IS passcode, but in this version nothing a client sends is forwarded: packets from clients are
/// ignored.
/// </para>
/// <para>
/// A client whose queue fills (<see cref="AprsIsServerOptions.ClientQueueLength"/>) is disconnected rather than
/// slowing the others, as APRS-IS servers do.
/// </para>
/// </remarks>
public sealed class AprsIsServer : IAsyncDisposable
{
    private readonly AprsIsServerOptions _options;
    private readonly ConcurrentDictionary<ClientSession, Task> _sessions = new();
    private readonly CancellationTokenSource _stopping = new();

    // Each round of a history replay filters what was published during the one before, so a few are enough.
    private const int MaxReplayRounds = 4;

    // Publishing and a client going live take turns, so a joining client gets each packet once, from history or live.
    private readonly Lock _publishing = new();
    private readonly Queue<PublishedPacket> _history = new();
    private readonly ConcurrentDictionary<IPacketSubscriber, byte> _subscribers = new();
    private TcpListener? _listener;
    private Task? _accepting;

    /// <summary>Creates a server; <see cref="Start"/> begins listening.</summary>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public AprsIsServer(AprsIsServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    /// <summary>
    /// The address the server is listening on, once started. With port 0 in
    /// <see cref="AprsIsServerOptions.EndPoint"/>, this gives the port the system chose.
    /// </summary>
    public IPEndPoint? LocalEndPoint => (IPEndPoint?)_listener?.LocalEndpoint;

    /// <summary>The server's name, as in <see cref="AprsIsServerOptions.ServerName"/>.</summary>
    internal string Name => _options.ServerName;

    /// <summary>The software name and version sent to clients.</summary>
    internal string SoftwareName => _options.SoftwareName;

    /// <inheritdoc cref="SoftwareName"/>
    internal string SoftwareVersion => _options.SoftwareVersion;

    /// <summary>The server's clock.</summary>
    internal TimeProvider TimeProvider => _options.TimeProvider;

    /// <summary>Clients connected now, including any still logging in.</summary>
    public int ClientCount => _sessions.Count;

    /// <summary>Clients disconnected so far because they fell too far behind.</summary>
    public long ClientsDroppedForOverflow => Interlocked.Read(ref _overflowed);

    private long _overflowed;

    /// <summary>Starts listening and accepting clients.</summary>
    /// <exception cref="InvalidOperationException">The server has already been started.</exception>
    /// <exception cref="SocketException">The endpoint cannot be bound, for example because it is in use.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_stopping.IsCancellationRequested, this);
        if (_listener is not null)
        {
            throw new InvalidOperationException("The server has already been started.");
        }

        _listener = new TcpListener(_options.EndPoint);
        _listener.Start();
        _accepting = AcceptAsync(_listener, _stopping.Token);
    }

    /// <summary>
    /// Sends <paramref name="packet"/> to every logged-in client whose filter passes it. Never blocks: a client that
    /// cannot keep up is disconnected.
    /// </summary>
    public void Publish(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        GatewayMetrics.Published(_options.ServerName);
        _options.Stations?.Observe(packet);
        var keepHistory = _options.History > TimeSpan.Zero;
        if (_sessions.IsEmpty && _subscribers.IsEmpty && !keepHistory)
        {
            return;
        }

        var published = new PublishedPacket(packet, _options.TimeProvider.GetUtcNow());
        lock (_publishing)
        {
            if (keepHistory)
            {
                _history.Enqueue(published);
                TrimHistory();
            }

            foreach (var session in _sessions.Keys)
            {
                session.Offer(packet, published.Line);
            }

            foreach (var subscriber in _subscribers.Keys)
            {
                subscriber.Offer(published);
            }
        }
    }

    /// <summary>
    /// Starts giving published packets to <paramref name="subscriber"/>, beginning with the history; each packet
    /// reaches it once, from the history or live.
    /// </summary>
    internal void Subscribe(IPacketSubscriber subscriber) =>
        Replay(subscriber.Offer, () => _subscribers[subscriber] = 0);

    /// <summary>Stops giving packets to <paramref name="subscriber"/>.</summary>
    internal void Unsubscribe(IPacketSubscriber subscriber) => _subscribers.TryRemove(subscriber, out _);

    /// <summary>The station store, for filters that measure from where stations were heard.</summary>
    internal StationStore? Stations => _options.Stations;

    /// <summary>Starts sending to a client that has logged in, beginning with the history its filter passes.</summary>
    internal void Join(ClientSession session, LoginLine login) =>
        Replay(published => session.Offer(published.Packet, published.Line, login), () => session.Activate(login));

    // A joining client's history goes through its filter outside the lock, so neither a long history nor a costly
    // filter holds up publishing. What is published meanwhile waits in a backlog and is filtered the same way. Once
    // the client has caught up, it goes live under the lock, so it gets each packet once and in order.
    private void Replay(Action<PublishedPacket> offer, Action goLive)
    {
        var backlog = new Backlog();
        List<PublishedPacket> pending;
        lock (_publishing)
        {
            TrimHistory();
            if (_history.Count == 0)
            {
                goLive();
                return;
            }

            pending = [.. _history];
            _subscribers[backlog] = 0;
        }

        try
        {
            for (var round = 1; ; round++)
            {
                foreach (var published in pending)
                {
                    offer(published);
                }

                lock (_publishing)
                {
                    pending = backlog.Take();
                    if (pending.Count == 0 || round == MaxReplayRounds)
                    {
                        // Only what was published while the last round was filtered, if the feed outran it.
                        foreach (var published in pending)
                        {
                            offer(published);
                        }

                        _subscribers.TryRemove(backlog, out _);
                        goLive();
                        return;
                    }
                }
            }
        }
        finally
        {
            _subscribers.TryRemove(backlog, out _);
        }
    }

    /// <summary>Holds what is published while a joining client's history is being filtered.</summary>
    private sealed class Backlog : IPacketSubscriber
    {
        private List<PublishedPacket> _packets = [];

        public void Offer(PublishedPacket packet) => _packets.Add(packet);

        public List<PublishedPacket> Take()
        {
            var taken = _packets;
            _packets = [];
            return taken;
        }
    }

    private void TrimHistory()
    {
        var oldest = _options.TimeProvider.GetUtcNow() - _options.History;
        while (
            _history.Count > _options.MaxHistoryPackets || (_history.TryPeek(out var first) && first.Received < oldest)
        )
        {
            _history.Dequeue();
        }
    }

    /// <summary>Stops listening and disconnects every client.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        if (_accepting is not null)
        {
            await _accepting.ConfigureAwait(false);
        }

        await Task.WhenAll(_sessions.Values).ConfigureAwait(false);
        _listener?.Dispose();
        _stopping.Dispose();
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(stopping).ConfigureAwait(false);
            }
            // Stopped before the loop got here: a listener that is no longer listening throws InvalidOperationException.
            catch (Exception e)
                when (e
                        is OperationCanceledException
                            or SocketException
                            or ObjectDisposedException
                            or InvalidOperationException
                )
            {
                return;
            }

            if (_sessions.Count >= _options.MaxClients)
            {
                GatewayMetrics.Disconnected(_options.ServerName, "server_full", wasConnected: false);
                await RefuseAsync(tcp).ConfigureAwait(false);
                continue;
            }

            // Registered before it starts, so a session that ends at once still finds itself to remove.
            var session = new ClientSession(tcp, _options, Join, stopping);
            GatewayMetrics.Connected(_options.ServerName);
            var run = new Task<Task>(() => RunAsync(session));
            _sessions[session] = run.Unwrap();
            run.Start(TaskScheduler.Default);
        }
    }

    private async Task RunAsync(ClientSession session)
    {
        try
        {
            await session.RunAsync().ConfigureAwait(false);
        }
        finally
        {
            if (session.Overflowed)
            {
                Interlocked.Increment(ref _overflowed);
            }

            _sessions.TryRemove(session, out _);
            GatewayMetrics.Disconnected(_options.ServerName, session.EndReason, wasConnected: true);
            session.Dispose();
        }
    }

    private static async Task RefuseAsync(TcpClient tcp)
    {
        using (tcp)
        {
            try
            {
                await tcp.GetStream().WriteAsync(Encoding.ASCII.GetBytes("# Server full\r\n")).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or SocketException)
            {
                // It left first.
            }
        }
    }
}
