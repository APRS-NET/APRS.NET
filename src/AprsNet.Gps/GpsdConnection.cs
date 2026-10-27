using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using AprsNet.Packets;

namespace AprsNet.Gps;

/// <summary>
/// A lasting connection to gpsd, on this machine or another, that yields a <see cref="GpsFix"/> for each position
/// report (TPV) it sends. When gpsd closes the connection, restarts or cannot be reached, it reconnects, waiting
/// <see cref="ReconnectDelay"/> and doubling up to <see cref="MaxReconnectDelay"/>, as <c>KissConnection</c> does; a
/// connection that stayed up at least <see cref="MaxReconnectDelay"/> starts the waits again. The read carries on
/// across reconnects until cancelled or disposed.
/// </summary>
/// <remarks>
/// <para>
/// It asks gpsd for JSON reports (<c>?WATCH={"enable":true,"json":true}</c>) on each connection. A fix's satellites
/// and HDOP come from gpsd's latest SKY report, which gpsd sends less often than TPV, so they can be a few seconds old.
/// Altitude is given only with a 3D fix.
/// </para>
/// <para>gpsd reports times in UTC, so <see cref="GpsFix.Time"/> is always known once the GPS has the time.</para>
/// <para>
/// Each fix is recorded on the <c>AprsNet.Gps</c> meter, tagged <c>gps</c> with <see cref="Server"/>: see
/// <see cref="GpsMetrics"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// await using var gps = new GpsdConnection("raspberrypi.local");
/// await foreach (var fix in gps.ReadFixesAsync(cancellationToken))
/// {
///     Console.WriteLine($"{fix.Mode} {fix.Position} {fix.SpeedKnots:0.#} kn");
/// }
/// </code>
/// </example>
public sealed class GpsdConnection : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<Stream>> _connect;
    private readonly string? _device;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _stateLock = new();
    private readonly CancellationTokenSource _disposing = new();
    private readonly GpsMetrics _metrics;
    private int _reading;
    private int _disposed;
    private int _connected;

    /// <summary>
    /// Creates a connection to gpsd at <paramref name="host"/>:<paramref name="port"/> with the other settings at
    /// their defaults; nothing connects until a read starts.
    /// </summary>
    /// <exception cref="ArgumentException">The host is empty or the port is not 1 to 65535.</exception>
    public GpsdConnection(string host = "localhost", int port = GpsdOptions.DefaultPort)
        : this(new GpsdOptions { Host = host, Port = port }) { }

    /// <summary>Creates a connection with <paramref name="options"/>; nothing connects until a read starts.</summary>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public GpsdConnection(GpsdOptions options)
        : this(ConnectTcp(Validated(options)), options) { }

    internal GpsdConnection(Func<CancellationToken, Task<Stream>> connect, GpsdOptions options)
    {
        Validated(options);
        _connect = connect;
        _device = options.Device;
        _timeProvider = options.TimeProvider;
        Server = $"{options.Host}:{options.Port}";
        _metrics = new GpsMetrics(Server, GpsErrorConfidence.Percent95, _timeProvider);
        ReconnectDelay = options.ReconnectDelay;
        MaxReconnectDelay = options.MaxReconnectDelay;
    }

    /// <summary>gpsd's address, as <c>host:port</c>.</summary>
    public string Server { get; }

    /// <summary>The wait before the first reconnect.</summary>
    public TimeSpan ReconnectDelay { get; }

    /// <summary>The longest wait between reconnect attempts.</summary>
    public TimeSpan MaxReconnectDelay { get; }

    /// <summary>Where the connection is: connecting, connected, waiting to reconnect, idle or closed.</summary>
    public GpsdConnectionState State { get; private set; } = GpsdConnectionState.Idle;

    /// <summary>Whether gpsd is connected now.</summary>
    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    /// <summary>How many times the read has connected again after its first connection.</summary>
    public int Reconnects { get; private set; }

    /// <summary>gpsd's version (<c>3.25</c>), once it has said; it stays set while reconnecting.</summary>
    public string? Release { get; private set; }

    /// <summary>
    /// The last error gpsd reported, such as an unknown <see cref="GpsdOptions.Device"/>; <see langword="null"/> if none.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// How good the fix is: the satellites used and in view and the DOPs from gpsd's latest SKY report, and the error
    /// estimates (<c>eph</c>, <c>epx</c>, <c>epy</c>, <c>epv</c>, <c>eps</c>) from its latest TPV. It is updated
    /// before each fix is yielded, and kept while reconnecting.
    /// </summary>
    public GpsQuality Quality { get; private set; } = new();

    /// <summary>
    /// The satellites in view, from gpsd's latest SKY report that listed them; kept while reconnecting. The list is
    /// replaced, never changed, so a new list means a new report.
    /// </summary>
    public IReadOnlyList<GpsSatellite> Satellites { get; private set; } = [];

    /// <summary>
    /// Raised for each line gpsd sends, as it came, before it is read: for a console showing gpsd's output. It is
    /// raised on the reading thread; keep handlers short.
    /// </summary>
    public event EventHandler<GpsdLineEventArgs>? LineRead;

    /// <summary>Raised when <see cref="State"/> changes. It is raised on the reading thread; keep handlers short.</summary>
    public event EventHandler<GpsdConnectionStateChangedEventArgs>? StateChanged;

    private bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    /// <summary>
    /// Yields a fix for each position report gpsd sends, across reconnects, until cancelled or disposed. Only one read
    /// can run at a time.
    /// </summary>
    /// <param name="cancellationToken">Ends the read.</param>
    /// <exception cref="InvalidOperationException">Another read is running.</exception>
    /// <exception cref="ObjectDisposedException">The connection is disposed.</exception>
    public async IAsyncEnumerable<GpsFix> ReadFixesAsync(
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
        SetState(GpsdConnectionState.Connecting);
        try
        {
            while (true)
            {
                Exception? failure = null;
                Stream? stream = null;
                try
                {
                    stream = await _connect(token).ConfigureAwait(false);
                    await stream
                        .WriteAsync(Encoding.ASCII.GetBytes(GpsdReports.Watch(_device)), token)
                        .ConfigureAwait(false);
                }
                catch (Exception e) when (IsLinkFailure(e))
                {
                    failure = e;
                    if (stream is not null)
                    {
                        await stream.DisposeAsync().ConfigureAwait(false);
                        stream = null;
                    }
                }
                catch (OperationCanceledException) when (IsDisposed && !cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                if (stream is not null)
                {
                    if (connectedBefore)
                    {
                        Reconnects++;
                    }

                    connectedBefore = true;
                    var connectedAt = _timeProvider.GetTimestamp();
                    var reports = new GpsdReports();
                    Volatile.Write(ref _connected, 1);
                    SetState(GpsdConnectionState.Connected);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    try
                    {
                        while (true)
                        {
                            string? line;
                            try
                            {
                                line = await reader.ReadLineAsync(token).ConfigureAwait(false);
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

                            if (line is null)
                            {
                                break; // gpsd closed the connection
                            }

                            LineRead?.Invoke(this, new GpsdLineEventArgs(line));
                            var (quality, satellites) = (reports.Quality, reports.Satellites);
                            var fix = reports.Read(line);
                            // A new connection starts its reports afresh; keep what the last one said until gpsd says more.
                            Quality = ReferenceEquals(reports.Quality, quality) ? Quality : reports.Quality;
                            Satellites = ReferenceEquals(reports.Satellites, satellites)
                                ? Satellites
                                : reports.Satellites;
                            Release = reports.Release ?? Release;
                            LastError = reports.LastError ?? LastError;
                            if (fix is not null)
                            {
                                _metrics.Record(fix, Quality, Satellites);
                                yield return fix;
                            }
                        }
                    }
                    finally
                    {
                        Volatile.Write(ref _connected, 0);
                        _metrics.Reset();
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

                SetState(GpsdConnectionState.Reconnecting, failure, delay);
                try
                {
                    await Task.Delay(delay, _timeProvider, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (IsDisposed && !cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));
                SetState(GpsdConnectionState.Connecting);
            }
        }
        finally
        {
            // A read that ends while the connection lives leaves it idle, ready for another; disposing closes it.
            if (!IsDisposed)
            {
                SetState(GpsdConnectionState.Idle);
            }

            Volatile.Write(ref _reading, 0);
        }
    }

    /// <summary>Ends any read and closes the connection.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _disposing.CancelAsync().ConfigureAwait(false);
        _disposing.Dispose();
        SetState(GpsdConnectionState.Closed);
    }

    private static GpsdOptions Validated(GpsdOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return options;
    }

    private static Func<CancellationToken, Task<Stream>> ConnectTcp(GpsdOptions options)
    {
        var (host, port, connectTimeout) = (options.Host, options.Port, options.ConnectTimeout);
        return async token =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            // gpsd sends nothing while no GPS is attached, so keepalives are what notice a dead link.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(connectTimeout);
            try
            {
                await socket.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                socket.Dispose();
                throw new TimeoutException($"Timed out connecting to gpsd at {host}:{port}.");
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
    }

    // What a lost or unreachable gpsd throws: a network error or a timeout.
    private static bool IsLinkFailure(Exception e) => e is SocketException or IOException or TimeoutException;

    private void SetState(GpsdConnectionState state, Exception? error = null, TimeSpan? retryIn = null)
    {
        lock (_stateLock)
        {
            if (State == state || State == GpsdConnectionState.Closed)
            {
                return;
            }

            State = state;
        }

        StateChanged?.Invoke(this, new GpsdConnectionStateChangedEventArgs(state, error, retryIn));
    }
}

/// <summary>Where a <see cref="GpsdConnection"/> is: see <see cref="GpsdConnection.State"/>.</summary>
public enum GpsdConnectionState
{
    /// <summary>Not reading: no read has started, or the last one ended.</summary>
    Idle,

    /// <summary>Connecting to gpsd.</summary>
    Connecting,

    /// <summary>Connected: gpsd's reports are flowing.</summary>
    Connected,

    /// <summary>
    /// The connection ended or could not be made, and the next attempt is waiting;
    /// <see cref="GpsdConnectionStateChangedEventArgs.RetryIn"/> says how long.
    /// </summary>
    Reconnecting,

    /// <summary>Disposed.</summary>
    Closed,
}

/// <summary>A line gpsd sent: see <see cref="GpsdConnection.LineRead"/>.</summary>
public sealed class GpsdLineEventArgs : EventArgs
{
    internal GpsdLineEventArgs(string line)
    {
        Line = line;
    }

    /// <summary>The line, without its line ending: one JSON report.</summary>
    public string Line { get; }
}

/// <summary>A change of <see cref="GpsdConnection.State"/>.</summary>
public sealed class GpsdConnectionStateChangedEventArgs : EventArgs
{
    internal GpsdConnectionStateChangedEventArgs(GpsdConnectionState state, Exception? error, TimeSpan? retryIn)
    {
        State = state;
        Error = error;
        RetryIn = retryIn;
    }

    /// <summary>The new state.</summary>
    public GpsdConnectionState State { get; }

    /// <summary>
    /// What ended the connection or attempt, when the state is <see cref="GpsdConnectionState.Reconnecting"/>:
    /// <see langword="null"/> when gpsd closed the connection cleanly.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>How long until the next attempt, when the state is <see cref="GpsdConnectionState.Reconnecting"/>.</summary>
    public TimeSpan? RetryIn { get; }
}
