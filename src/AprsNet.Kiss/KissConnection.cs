using System.Net.Sockets;
using System.Runtime.CompilerServices;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss;

/// <summary>
/// A lasting connection to a KISS TNC: when the TNC closes the connection, goes away or cannot be reached, it
/// reconnects, waiting <see cref="ReconnectDelay"/> and doubling up to <see cref="MaxReconnectDelay"/>. A connection
/// that stayed up at least <see cref="MaxReconnectDelay"/> starts the waits again from <see cref="ReconnectDelay"/>;
/// one the TNC drops straight away does not, so a TNC that accepts and then closes every connection is retried less
/// and less often. The read streams carry on across reconnects until cancelled or disposed, so a restarted Dire Wolf,
/// a soundmodem that starts after the app, or a USB TNC unplugged and plugged back in only pauses them.
/// </summary>
/// <remarks>
/// <see cref="KissTcpConnection"/> reaches a TNC over TCP and <c>KissSerialConnection</c> (in <c>AprsNet.Kiss.Serial</c>) on a serial port; an
/// app that takes either can use this type.
/// </remarks>
public abstract class KissConnection : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<Stream>> _connect;
    private readonly int _tncPort;
    private readonly TimeProvider _timeProvider;
    private readonly string _tnc;
    private readonly Lock _stateLock = new();
    private readonly CancellationTokenSource _disposing = new();
    private KissStreamClient? _current;
    private int _reading;
    private int _disposed;

    private protected KissConnection(
        Func<CancellationToken, Task<Stream>> connect,
        string tnc,
        int tncPort,
        TimeSpan reconnectDelay,
        TimeSpan maxReconnectDelay,
        TimeProvider timeProvider,
        KissTransmitLimit transmitLimit
    )
    {
        _connect = connect;
        _tnc = tnc;
        _tncPort = tncPort;
        ReconnectDelay = reconnectDelay;
        MaxReconnectDelay = maxReconnectDelay;
        _timeProvider = timeProvider;
        TransmitLimit = transmitLimit;
    }

    /// <summary>How fast frames may be sent; one limit covers every connection, and a send waits for its turn.</summary>
    public KissTransmitLimit TransmitLimit { get; }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>The first wait before reconnecting.</summary>
    public TimeSpan ReconnectDelay { get; }

    /// <summary>The longest wait between attempts.</summary>
    public TimeSpan MaxReconnectDelay { get; }

    /// <summary>Where the connection is now: not reading, connecting, connected, waiting to reconnect, or closed.</summary>
    public KissConnectionState State { get; private set; } = KissConnectionState.Idle;

    /// <summary>Whether the TNC is connected now (<see cref="State"/> is <see cref="KissConnectionState.Connected"/>).</summary>
    public bool IsConnected => Volatile.Read(ref _current) is not null;

    /// <summary>Connections made after the first: one each time the TNC was lost and reached again.</summary>
    public int Reconnects { get; private set; }

    /// <summary>
    /// Raised when <see cref="State"/> changes. For <see cref="KissConnectionState.Reconnecting"/> it says what ended
    /// the connection or attempt and how long until the next one. Raised on the reading thread; keep it short.
    /// </summary>
    public event EventHandler<KissConnectionStateChangedEventArgs>? StateChanged;

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
        SetState(KissConnectionState.Connecting);
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
                    )
                    {
                        MetricsTnc = _tnc,
                    };
                }
                catch (Exception e) when (IsLinkFailure(e))
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
                    KissMetrics.Active(_tnc, +1);
                    SetState(KissConnectionState.Connected);
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
                                catch (Exception e) when (IsLinkFailure(e))
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
                            KissMetrics.Active(_tnc, -1);
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

                KissMetrics.Reconnect(
                    _tnc,
                    client is null ? "connect_failed"
                        : failure is null ? "tnc_closed"
                        : "socket_error"
                );
                SetState(KissConnectionState.Reconnecting, failure, delay);
                try
                {
                    await Task.Delay(delay, _timeProvider, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (IsDisposed && !cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));
                SetState(KissConnectionState.Connecting);
            }
        }
        finally
        {
            // A read that ends while the connection lives leaves it idle, ready for another; disposing closes it.
            if (!IsDisposed)
            {
                SetState(KissConnectionState.Idle);
            }

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
        SetState(KissConnectionState.Closed);
        GC.SuppressFinalize(this);
    }

    // What a lost or unreachable TNC throws: a network error, a timeout, a serial port that went away (IOException),
    // or one another program holds or the user may not open.
    private static bool IsLinkFailure(Exception e) =>
        e is SocketException or IOException or TimeoutException or UnauthorizedAccessException;

    private void SetState(KissConnectionState state, Exception? error = null, TimeSpan? retryIn = null)
    {
        lock (_stateLock)
        {
            if (State == state || State == KissConnectionState.Closed)
            {
                return;
            }

            State = state;
        }

        StateChanged?.Invoke(this, new KissConnectionStateChangedEventArgs(state, error, retryIn));
    }
}

/// <summary>Where a <see cref="KissConnection"/> is: see <see cref="KissConnection.State"/>.</summary>
public enum KissConnectionState
{
    /// <summary>Not reading: no read has started, or the last one ended.</summary>
    Idle,

    /// <summary>Connecting to the TNC.</summary>
    Connecting,

    /// <summary>Connected: frames are flowing and can be sent.</summary>
    Connected,

    /// <summary>
    /// The connection ended or could not be made, and the next attempt is waiting;
    /// <see cref="KissConnectionStateChangedEventArgs.RetryIn"/> says how long.
    /// </summary>
    Reconnecting,

    /// <summary>Disposed.</summary>
    Closed,
}

/// <summary>A change of <see cref="KissConnection.State"/>.</summary>
public sealed class KissConnectionStateChangedEventArgs : EventArgs
{
    internal KissConnectionStateChangedEventArgs(KissConnectionState state, Exception? error, TimeSpan? retryIn)
    {
        State = state;
        Error = error;
        RetryIn = retryIn;
    }

    /// <summary>The new state.</summary>
    public KissConnectionState State { get; }

    /// <summary>
    /// What ended the connection or attempt, when the state is <see cref="KissConnectionState.Reconnecting"/>:
    /// <see langword="null"/> when the TNC closed the connection cleanly.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>How long until the next attempt, when the state is <see cref="KissConnectionState.Reconnecting"/>.</summary>
    public TimeSpan? RetryIn { get; }
}
