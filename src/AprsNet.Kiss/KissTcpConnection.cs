using System.Net.Sockets;
using System.Runtime.CompilerServices;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss;

/// <summary>
/// A lasting connection to a KISS TNC over TCP: like <see cref="KissTcpClient"/>, but when the TNC closes the
/// connection or cannot be reached it reconnects, waiting <see cref="ReconnectDelay"/> and doubling up to
/// <see cref="MaxReconnectDelay"/>. A connection that stayed up at least <see cref="MaxReconnectDelay"/> starts the
/// waits again from <see cref="ReconnectDelay"/>; one the TNC drops straight away does not, so a TNC that accepts and
/// then closes every connection is retried less and less often. The read streams carry on across reconnects until cancelled or disposed, so a
/// restarted Dire Wolf or a soundmodem that starts after the app only pauses them.
/// </summary>
public sealed class KissTcpConnection : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<Stream>> _connect;
    private readonly int _tncPort;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _disposing = new();
    private KissStreamClient? _current;
    private int _reading;
    private int _disposed;

    /// <summary>
    /// Creates a connection to the TNC at <paramref name="host"/>:<paramref name="port"/> with the other settings at
    /// their defaults; nothing connects until a read starts.
    /// </summary>
    /// <exception cref="ArgumentException">The host is empty or the port is not 1 to 65535.</exception>
    public KissTcpConnection(string host, int port = KissTcpClient.DefaultPort)
        : this(new KissTcpOptions { Host = host, Port = port }) { }

    /// <summary>Creates a connection with <paramref name="options"/>; nothing connects until a read starts.</summary>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public KissTcpConnection(KissTcpOptions options)
        : this(ConnectTcp(Validated(options)), options) { }

    internal KissTcpConnection(Func<CancellationToken, Task<Stream>> connect, KissTcpOptions options)
    {
        Validated(options);
        ReconnectDelay = options.ReconnectDelay;
        MaxReconnectDelay = options.MaxReconnectDelay;
        _connect = connect;
        _tncPort = options.TncPort;
        _timeProvider = options.TimeProvider;
        TransmitLimit = options.CreateTransmitLimit();
    }

    private static KissTcpOptions Validated(KissTcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return options;
    }

    /// <summary>How fast frames may be sent; one limit covers every connection, and a send waits for its turn.</summary>
    public KissTransmitLimit TransmitLimit { get; }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>The first wait before reconnecting.</summary>
    public TimeSpan ReconnectDelay { get; }

    /// <summary>The longest wait between attempts.</summary>
    public TimeSpan MaxReconnectDelay { get; }

    /// <summary>Whether the TNC is connected now.</summary>
    public bool IsConnected => Volatile.Read(ref _current) is not null;

    /// <summary>Connections made after the first: one each time the TNC was lost and reached again.</summary>
    public int Reconnects { get; private set; }

    /// <summary>
    /// Raised when the TNC is lost or cannot be reached, with the exception (<see langword="null"/> when the TNC closed
    /// the connection cleanly) and the wait before the next attempt. Raised on the reading thread; keep it short.
    /// </summary>
    public event EventHandler<KissConnectionLostEventArgs>? ConnectionLost;

    /// <summary>
    /// Yields every AX.25 frame the TNC passes on, connecting and reconnecting as needed. Disposing the connection
    /// ends the stream; cancelling <paramref name="cancellationToken"/> throws <see cref="OperationCanceledException"/>.
    /// One reader at a time.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another read is already running.</exception>
    public async IAsyncEnumerable<Ax25Frame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (Interlocked.Exchange(ref _reading, 1) != 0)
        {
            throw new InvalidOperationException("Only one read can run at a time.");
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposing.Token);
        var token = stop.Token;
        var delay = ReconnectDelay;
        var connectedBefore = false;
        try
        {
            while (true)
            {
                Exception? failure = null;
                KissStreamClient? client = null;
                try
                {
                    // This connection applies the limit itself, so it holds across reconnects.
                    client = new KissStreamClient(
                        await _connect(token).ConfigureAwait(false),
                        _tncPort,
                        transmitLimit: KissTransmitLimit.Off
                    );
                }
                catch (Exception e) when (e is SocketException or IOException or TimeoutException)
                {
                    failure = e;
                }
                catch (OperationCanceledException) when (IsDisposed && !cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                if (client is not null)
                {
                    if (connectedBefore)
                    {
                        Reconnects++;
                    }

                    connectedBefore = true;
                    var connectedAt = _timeProvider.GetTimestamp();
                    Volatile.Write(ref _current, client);
                    await using (client.ConfigureAwait(false))
                    {
                        var frames = client.ReadFramesAsync(token).GetAsyncEnumerator(token);
                        try
                        {
                            while (true)
                            {
                                try
                                {
                                    if (!await frames.MoveNextAsync().ConfigureAwait(false))
                                    {
                                        break;
                                    }
                                }
                                catch (Exception e) when (e is SocketException or IOException)
                                {
                                    failure = e;
                                    break;
                                }
                                catch (OperationCanceledException)
                                    when (IsDisposed && !cancellationToken.IsCancellationRequested)
                                {
                                    break;
                                }

                                yield return frames.Current;
                            }
                        }
                        finally
                        {
                            Volatile.Write(ref _current, null);
                            await frames.DisposeAsync().ConfigureAwait(false);
                        }
                    }

                    // A connection that held for a while was not part of a failing streak: start the backoff again.
                    if (_timeProvider.GetElapsedTime(connectedAt) >= MaxReconnectDelay)
                    {
                        delay = ReconnectDelay;
                    }
                }

                // Disposing ends the stream; the caller's own cancellation throws, as usual.
                cancellationToken.ThrowIfCancellationRequested();
                if (IsDisposed)
                {
                    yield break;
                }

                ConnectionLost?.Invoke(this, new KissConnectionLostEventArgs(failure, delay));
                try
                {
                    await Task.Delay(delay, _timeProvider, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (IsDisposed && !cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));
            }
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    /// <summary>
    /// Yields the APRS packets among the frames, each parsed from its TNC2 line, across reconnects. Use this or
    /// <see cref="ReadFramesAsync"/>, not both.
    /// </summary>
    public async IAsyncEnumerable<AprsPacket> ReadPacketsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var frame in ReadFramesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (frame.IsAprs)
            {
                yield return frame.ToAprsPacket();
            }
        }
    }

    /// <summary>Sends <paramref name="packet"/> for the TNC to transmit, once <see cref="TransmitLimit"/> allows.</summary>
    /// <exception cref="InvalidOperationException">The TNC is not connected now; nothing was sent.</exception>
    /// <exception cref="ArgumentException">The packet cannot be sent as AX.25; see <see cref="Ax25Frame.FromAprsPacket"/>.</exception>
    public ValueTask SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return SendFrameAsync(Ax25Frame.FromAprsPacket(packet), cancellationToken);
    }

    /// <summary>Sends an AX.25 frame for the TNC to transmit, once <see cref="TransmitLimit"/> allows.</summary>
    /// <exception cref="InvalidOperationException">
    /// The TNC is not connected now; nothing was sent. Checked before and after waiting for the limit.
    /// </exception>
    public async ValueTask SendFrameAsync(Ax25Frame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _ = Connected;
        await TransmitLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await Connected.SendFrameAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private KissStreamClient Connected =>
        Volatile.Read(ref _current)
        ?? throw new InvalidOperationException("The TNC is not connected; start reading to connect.");

    /// <summary>Ends any read and closes the connection.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _disposing.CancelAsync().ConfigureAwait(false);
        _disposing.Dispose();
    }

    private static Func<CancellationToken, Task<Stream>> ConnectTcp(KissTcpOptions options)
    {
        var (host, port, connectTimeout) = (options.Host, options.Port, options.ConnectTimeout);
        return async token =>
        {
            var client = new TcpClient { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(connectTimeout);
            try
            {
                await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                return new OwningStream(client);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                client.Dispose();
                throw new TimeoutException($"Timed out connecting to the TNC at {host}:{port}.");
            }
            catch
            {
                client.Dispose();
                throw;
            }
        };
    }

    /// <summary>A TCP client's stream that closes the client with it.</summary>
    private sealed class OwningStream(TcpClient client) : Stream
    {
        private readonly NetworkStream _stream = client.GetStream();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _stream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _stream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _stream.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => _stream.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream.Dispose();
                client.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>Why a <see cref="KissTcpConnection"/> lost or could not reach its TNC, and when it tries again.</summary>
public sealed class KissConnectionLostEventArgs(Exception? error, TimeSpan retryIn) : EventArgs
{
    /// <summary>What went wrong, or <see langword="null"/> when the TNC closed the connection.</summary>
    public Exception? Error { get; } = error;

    /// <summary>How long until the next attempt.</summary>
    public TimeSpan RetryIn { get; } = retryIn;
}
