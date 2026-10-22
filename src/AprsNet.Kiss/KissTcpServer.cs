using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss;

/// <summary>
/// A KISS TCP service, as Dire Wolf offers on port 8001: several KISS apps connect at once, each receives every frame
/// given to <see cref="Publish"/>, and the frames they send go to <see cref="Transmit"/>. Put it in front of one TNC so
/// several apps can share it, or fan an APRS-IS feed out to apps that only speak KISS with
/// <see cref="PublishFromAprsIs"/>.
/// </summary>
/// <remarks>
/// <para>
/// To share a TNC, publish what it hears and transmit through it, so its <see cref="KissTransmitLimit"/> covers every
/// app:
/// <code>
/// await using var server = new KissTcpServer(new IPEndPoint(IPAddress.Loopback, 8101)) { Transmit = tnc.SendFrameAsync };
/// server.Start();
/// await foreach (var frame in tnc.ReadFramesAsync()) server.Publish(frame);
/// </code>
/// </para>
/// <para>
/// Frames are sent to apps on KISS port 0. Only data frames with a valid AX.25 frame are taken from apps, whatever port
/// they name; KISS commands that set TNC parameters are ignored. A frame one app sends is not echoed to the others.
/// An app whose queue fills (<c>clientQueueLength</c> frames) is disconnected rather than slowing the others.
/// </para>
/// </remarks>
public sealed class KissTcpServer : IAsyncDisposable
{
    private readonly IPEndPoint _endPoint;
    private readonly int _maxClients;
    private readonly int _clientQueueLength;
    private readonly ConcurrentDictionary<Client, Task> _clients = new();
    private readonly CancellationTokenSource _stopping = new();
    private TcpListener? _listener;
    private string _server = "";
    private Task? _accepting;
    private long _fromClients;
    private long _transmitFailures;
    private long _overflowed;
    private long _refused;

    /// <summary>Creates a server; <see cref="Start"/> begins listening.</summary>
    /// <param name="endPoint">Where to listen; port 0 lets the system choose (see <see cref="LocalEndPoint"/>).</param>
    /// <param name="maxClients">How many apps may connect at once; 16 by default. Further connections are closed.</param>
    /// <param name="clientQueueLength">How many frames may wait for one app before it is disconnected; 1000 by default.</param>
    public KissTcpServer(IPEndPoint endPoint, int maxClients = 16, int clientQueueLength = 1000)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxClients, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(clientQueueLength, 1);
        _endPoint = endPoint;
        _maxClients = maxClients;
        _clientQueueLength = clientQueueLength;
    }

    /// <summary>
    /// Where the AX.25 frames apps send go, usually a TNC connection's <c>SendFrameAsync</c>. Each app's frames are
    /// passed on one at a time, in order. A failure is counted in <see cref="TransmitFailures"/> and the frame
    /// dropped. When <see langword="null"/>, frames from apps are counted and dropped.
    /// </summary>
    public Func<Ax25Frame, CancellationToken, ValueTask>? Transmit { get; init; }

    /// <summary>The address the server is listening on, once started.</summary>
    public IPEndPoint? LocalEndPoint => (IPEndPoint?)_listener?.LocalEndpoint;

    /// <summary>Apps connected now.</summary>
    public int ClientCount => _clients.Count;

    /// <summary>AX.25 frames received from apps so far.</summary>
    public long FramesFromClients => Interlocked.Read(ref _fromClients);

    /// <summary>Frames from apps that <see cref="Transmit"/> failed to send, such as while the TNC was not connected.</summary>
    public long TransmitFailures => Interlocked.Read(ref _transmitFailures);

    /// <summary>Apps disconnected so far because they fell too far behind.</summary>
    public long ClientsDroppedForOverflow => Interlocked.Read(ref _overflowed);

    /// <summary>Connections closed so far because <c>maxClients</c> apps were already connected.</summary>
    public long ClientsRefused => Interlocked.Read(ref _refused);

    /// <summary>
    /// An app connected. Raised on the thread that accepted it, before it is sent any frame. A handler that throws
    /// doesn't stop the server.
    /// </summary>
    public event EventHandler<KissTcpServerClientEventArgs>? ClientConnected;

    /// <summary>
    /// An app's connection ended: it left, fell too far behind (<see cref="KissTcpServerClientEventArgs.FellBehind"/>),
    /// or the server is stopping. A handler that throws doesn't stop the server.
    /// </summary>
    public event EventHandler<KissTcpServerClientEventArgs>? ClientDisconnected;

    /// <summary>
    /// A connection was closed at once because <c>maxClients</c> apps were already connected. A handler that throws
    /// doesn't stop the server.
    /// </summary>
    public event EventHandler<KissTcpServerClientEventArgs>? ClientRefused;

    /// <summary>Starts listening and accepting apps.</summary>
    /// <exception cref="InvalidOperationException">The server has already been started.</exception>
    /// <exception cref="SocketException">The endpoint cannot be bound, for example because it is in use.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_stopping.IsCancellationRequested, this);
        if (_listener is not null)
        {
            throw new InvalidOperationException("The server has already been started.");
        }

        _listener = new TcpListener(_endPoint);
        _listener.Start();
        _server = _listener.LocalEndpoint.ToString()!;
        _accepting = AcceptAsync(_listener, _stopping.Token);
    }

    /// <summary>Sends <paramref name="frame"/> to every connected app. Never blocks.</summary>
    public void Publish(Ax25Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_clients.IsEmpty)
        {
            return;
        }

        var bytes = KissFrame.ForAx25(frame).Encode();
        KissMetrics.ServerPublished(_server);
        foreach (var client in _clients.Keys)
        {
            if (!client.Offer(bytes))
            {
                Interlocked.Increment(ref _overflowed);
            }
        }
    }

    /// <summary>
    /// Sends a packet heard on APRS-IS to every connected app as third-party traffic from <paramref name="gateway"/>,
    /// the way an IGate passes internet traffic to RF: <c>GATEWAY&gt;TOCALL:}SOURCE&gt;DEST,TCPIP,GATEWAY*:…</c>.
    /// The gateway's path is not used.
    /// </summary>
    /// <param name="packet">The packet, with its APRS-IS header.</param>
    /// <param name="gateway">The callsign and TOCALL to send as; the callsign must fit AX.25.</param>
    /// <exception cref="ArgumentException">
    /// The packet has no header, or the result cannot be carried in AX.25; see <see cref="Ax25Frame.FromAprsPacket"/>.
    /// </exception>
    public void PublishFromAprsIs(AprsPacket packet, PacketAddress gateway)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(gateway);
        if (!packet.HasHeader)
        {
            throw new ArgumentException("The packet needs a SOURCE>DESTINATION header.", nameof(packet));
        }

        var header = Encoding.ASCII.GetBytes(
            $"{packet.Source!.Value}>{packet.Destination!.Value},TCPIP,{gateway.Source.Value}*:"
        );
        var inner = AprsPacket.Parse([.. header, .. packet.Information.Span]);
        var outer = AprsPacket.Create(gateway.Source, gateway.Destination, [], new ThirdPartyPayload(inner));
        Publish(Ax25Frame.FromAprsPacket(outer));
    }

    /// <summary>Stops listening and disconnects every app.</summary>
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

        await Task.WhenAll(_clients.Values).ConfigureAwait(false);
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
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            var remote = RemoteEndPoint(tcp);
            if (_clients.Count >= _maxClients)
            {
                tcp.Dispose();
                Interlocked.Increment(ref _refused);
                KissMetrics.ServerDisconnected(_server, "server_full");
                Raise(ClientRefused, new KissTcpServerClientEventArgs(remote, fellBehind: false));
                continue;
            }

            tcp.NoDelay = true;
            var client = new Client(tcp, _clientQueueLength, remote);
            // Registered before it starts, so a client that ends at once still finds itself to remove.
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _clients[client] = done.Task;
            KissMetrics.ServerClients(_server, 1);
            Raise(ClientConnected, new KissTcpServerClientEventArgs(remote, fellBehind: false));
            _ = RunAsync(client, done, stopping);
        }
    }

    private async Task RunAsync(Client client, TaskCompletionSource done, CancellationToken stopping)
    {
        try
        {
            await client.RunAsync(ReceivedAsync, stopping).ConfigureAwait(false);
        }
        finally
        {
            _clients.TryRemove(client, out _);
            client.Dispose();
            KissMetrics.ServerClients(_server, -1);
            KissMetrics.ServerDisconnected(
                _server,
                client.FellBehind ? "overflow"
                    : stopping.IsCancellationRequested ? "server_stopping"
                    : "closed"
            );
            Raise(ClientDisconnected, new KissTcpServerClientEventArgs(client.RemoteEndPoint, client.FellBehind));
            done.TrySetResult();
        }
    }

    private static IPEndPoint RemoteEndPoint(TcpClient tcp)
    {
        try
        {
            return tcp.Client.RemoteEndPoint as IPEndPoint ?? new IPEndPoint(IPAddress.None, 0);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            return new IPEndPoint(IPAddress.None, 0); // already gone
        }
    }

    private void Raise(EventHandler<KissTcpServerClientEventArgs>? handler, KissTcpServerClientEventArgs args)
    {
        try
        {
            handler?.Invoke(this, args);
        }
#pragma warning disable CA1031 // An app's handler must not stop the server.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Ignored, as documented on the events.
        }
    }

    private async ValueTask ReceivedAsync(Ax25Frame frame, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _fromClients);
        KissMetrics.ServerFromClients(_server);
        if (Transmit is null)
        {
            return;
        }

        try
        {
            await Transmit(frame, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _transmitFailures);
            KissMetrics.ServerTransmitFailure(_server);
        }
    }

    /// <summary>One connected app: a queue of frames to write, and a reader for the frames it sends.</summary>
    private sealed class Client(TcpClient tcp, int queueLength, IPEndPoint remoteEndPoint) : IDisposable
    {
        private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(queueLength) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite }
        );
        private readonly CancellationTokenSource _ended = new();
        private int _queued;
        private int _fellBehind;

        public IPEndPoint RemoteEndPoint => remoteEndPoint;

        /// <summary>Whether the client was ended because its queue filled.</summary>
        public bool FellBehind => Volatile.Read(ref _fellBehind) == 1;

        /// <summary>Queues a frame; on a full queue, ends the client and returns <see langword="false"/>.</summary>
        public bool Offer(byte[] bytes)
        {
            if (_ended.IsCancellationRequested)
            {
                return true;
            }

            if (Interlocked.Increment(ref _queued) > queueLength)
            {
                Volatile.Write(ref _fellBehind, 1);
                End();
                return false;
            }

            _outgoing.Writer.TryWrite(bytes);
            return true;
        }

        public async Task RunAsync(Func<Ax25Frame, CancellationToken, ValueTask> received, CancellationToken stopping)
        {
            using var run = CancellationTokenSource.CreateLinkedTokenSource(stopping, _ended.Token);
            var stream = tcp.GetStream();
            var writing = WriteAsync(stream, run.Token);
            try
            {
                await ReadAsync(stream, received, run.Token).ConfigureAwait(false);
            }
            catch (Exception e)
                when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // The app left, fell behind, or the server is stopping.
            }
            finally
            {
                End();
                await writing.ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            tcp.Dispose();
            _ended.Dispose();
        }

        private void End()
        {
            try
            {
                _ended.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already ended and disposed.
            }

            _outgoing.Writer.TryComplete();
        }

        private static async Task ReadAsync(
            NetworkStream stream,
            Func<Ax25Frame, CancellationToken, ValueTask> received,
            CancellationToken cancellationToken
        )
        {
            var decoder = new KissDecoder();
            var buffer = new byte[4096];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                foreach (var kiss in decoder.Decode(buffer.AsSpan(0, read)))
                {
                    if (kiss.TryGetAx25(out var frame))
                    {
                        await received(frame!, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task WriteAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var bytes in _outgoing.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    Interlocked.Decrement(ref _queued);
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
                when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // The app left, fell behind, or the server is stopping.
            }
            finally
            {
                End();
                // Unblocks a read waiting on an app that has stopped reading.
                tcp.Client.Close();
            }
        }
    }
}

/// <summary>An app connecting to a <see cref="KissTcpServer"/>, leaving it, or being turned away.</summary>
public sealed class KissTcpServerClientEventArgs : EventArgs
{
    internal KissTcpServerClientEventArgs(IPEndPoint remoteEndPoint, bool fellBehind)
    {
        RemoteEndPoint = remoteEndPoint;
        FellBehind = fellBehind;
    }

    /// <summary>The app's address and port.</summary>
    public IPEndPoint RemoteEndPoint { get; }

    /// <summary>
    /// For <see cref="KissTcpServer.ClientDisconnected"/>: whether the server ended the connection because the app fell
    /// too far behind reading frames.
    /// </summary>
    public bool FellBehind { get; }
}
