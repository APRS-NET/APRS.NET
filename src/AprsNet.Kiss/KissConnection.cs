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
/// <see cref="KissTcpConnection"/> reaches a TNC over TCP, <c>KissSerialConnection</c> (in <c>AprsNet.Kiss.Serial</c>) on a serial port,
/// and <see cref="AgwpeConnection"/> through an AGWPE server; an app that takes any of them can use this type.
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
    private int _textWarning;
    private readonly bool _reopenWhenSendingText;
    private int _manual;
    private int _scriptRunning;
    private readonly bool _textMode;
    private readonly bool _agwpe;
    private readonly ITimer _quietTimer;
    private readonly Lock _quietLock = new();
    private long _lastData;
    private bool _quiet;
    private bool _watchingQuiet;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, KissTransmitLimit> _portLimits = new();
    private readonly ITimer _gpsTimer;
    private readonly Lock _gpsLock = new();
    private long _lastSentence;
    private bool _gpsPresent;

    /// <summary>The default <c>GpsTimeout</c>: how long without a GPS sentence counts as the GPS gone.</summary>
    internal static readonly TimeSpan DefaultGpsTimeout = TimeSpan.FromSeconds(10);

    private protected KissConnection(
        Func<CancellationToken, Task<Stream>> connect,
        string tnc,
        int tncPort,
        TimeSpan reconnectDelay,
        TimeSpan maxReconnectDelay,
        TimeProvider timeProvider,
        KissTransmitLimit transmitLimit,
        TimeSpan? quietWarning,
        bool reopenWhenSendingText = false,
        bool textMode = false,
        TimeSpan? gpsTimeout = null,
        bool allPorts = false,
        bool agwpe = false
    )
    {
        AllPorts = allPorts;
        _agwpe = agwpe;
        TncPort = tncPort;
        _textMode = textMode;
        GpsTimeout = gpsTimeout ?? DefaultGpsTimeout;
        _gpsTimer = timeProvider.CreateTimer(_ => CheckGps(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _reopenWhenSendingText = reopenWhenSendingText;
        QuietWarning = quietWarning;
        _quietTimer = timeProvider.CreateTimer(
            _ => CheckQuiet(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );
        _connect = connect;
        _tnc = tnc;
        _tncPort = tncPort;
        ReconnectDelay = reconnectDelay;
        MaxReconnectDelay = maxReconnectDelay;
        _timeProvider = timeProvider;
        TransmitLimit = transmitLimit;
    }

    /// <summary>
    /// How fast frames may be sent to <see cref="TncPort"/>; one limit covers every connection, and a send waits for its
    /// turn. Each other radio port gets a limit of its own with the same settings.
    /// </summary>
    public KissTransmitLimit TransmitLimit { get; }

    /// <summary>The radio port, 0 to 15, that <see cref="SendAsync"/> sends on, and the only one read without <see cref="AllPorts"/>.</summary>
    public int TncPort { get; }

    /// <summary>
    /// Whether frames heard on every radio port of a multi-port TNC are read: <see cref="ReadPortFramesAsync"/> says which
    /// port each came from.
    /// </summary>
    public bool AllPorts { get; }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>The first wait before reconnecting.</summary>
    public TimeSpan ReconnectDelay { get; }

    /// <summary>The longest wait between attempts.</summary>
    public TimeSpan MaxReconnectDelay { get; }

    /// <summary>Where the connection is now: not reading, connecting, connected, waiting to reconnect, or closed.</summary>
    public KissConnectionState State { get; private set; } = KissConnectionState.Idle;

    /// <summary>Whether the TNC is connected now (<see cref="State"/> is <see cref="KissConnectionState.Connected"/>).</summary>
    public bool IsConnected => Volatile.Read(ref _current) is not null;

    /// <summary>
    /// How long the TNC may send nothing before <see cref="Warning"/> reports <see cref="KissWarning.Quiet"/>, or
    /// <see langword="null"/> when that is off. Only bytes count, so a TNC sending text in the wrong mode isn't quiet.
    /// </summary>
    public TimeSpan? QuietWarning { get; }

    /// <summary>Whether the <see cref="KissWarning.Quiet"/> warning is on now: nothing has come for <see cref="QuietWarning"/>.</summary>
    public bool IsQuiet
    {
        get
        {
            lock (_quietLock)
            {
                return _quiet;
            }
        }
    }

    /// <summary>
    /// Whether the <see cref="KissWarning.TextNotKiss"/> warning is on now: the TNC is sending text, not KISS frames, so
    /// nothing it sends can be read. It stays on across reconnects until a KISS frame arrives.
    /// </summary>
    public bool IsSendingText => Volatile.Read(ref _textWarning) == 1;

    /// <summary>
    /// How long without a GPS sentence counts as the GPS gone, for <see cref="GpsPresenceChanged"/>. Ten seconds unless
    /// the options say otherwise.
    /// </summary>
    public TimeSpan GpsTimeout { get; }

    /// <summary>
    /// Whether the TNC is sending GPS sentences now: one has come within <see cref="GpsTimeout"/>. Only a serial
    /// connection in text mode reads them; see <see cref="NmeaSentenceReceived"/>.
    /// </summary>
    public bool IsGpsPresent
    {
        get
        {
            lock (_gpsLock)
            {
                return _gpsPresent;
            }
        }
    }

    /// <summary>
    /// Each GPS sentence the TNC prints, across reconnects: a radio in its APRS mode with GPS PC output on, read in text
    /// mode (<c>KissSerialOptions.TextMode</c>), sends them between its packet lines. Only whole sentences whose
    /// checksum matches are raised; damaged ones show in <see cref="Traffic"/> as <see cref="KissTrafficKind.DamagedNmea"/>.
    /// A sentence says a GPS is there, not that it has a fix: a GPS without one still sends sentences, with no position
    /// or a placeholder one, so feed them to an <c>NmeaFixTracker</c> and use its fix. Raised on the reading thread;
    /// an exception thrown by a handler is ignored. A TNC in KISS mode sends none: KISS has no room for them.
    /// </summary>
    public event EventHandler<NmeaSentenceEventArgs>? NmeaSentenceReceived;

    /// <summary>
    /// Raised when the TNC starts sending GPS sentences, and again when none has come for <see cref="GpsTimeout"/>, so an
    /// app can show a GPS while there is one, or warn when one it expects is missing. On the reading thread when the
    /// first sentence arrives, on a timer thread when they stop.
    /// </summary>
    public event EventHandler<GpsPresenceChangedEventArgs>? GpsPresenceChanged;

    /// <summary>Connections made after the first: one each time the TNC was lost and reached again.</summary>
    public int Reconnects { get; private set; }

    /// <summary>
    /// Raised when <see cref="State"/> changes. For <see cref="KissConnectionState.Reconnecting"/> it says what ended
    /// the connection or attempt and how long until the next one. Raised on the reading thread; keep it short.
    /// </summary>
    public event EventHandler<KissConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Fires when the TNC starts sending something other than KISS frames, and again when it stops, on the reading
    /// thread. Across reconnects a warning fires once, and is over when frames arrive again on any connection. See
    /// <see cref="KissWarning"/>.
    /// </summary>
    public event EventHandler<KissWarningEventArgs>? Warning;

    /// <summary>
    /// Everything that crosses the link, across reconnects, for a troubleshooting console: each frame received (APRS or
    /// not, for this port or another, damaged), bytes outside any frame a line at a time, each frame sent, and each
    /// step of a TNC's setup. Received entries are raised on the reading thread, sent ones on the sending thread, setup
    /// steps while the port opens. Nothing extra is done while no one is subscribed, and nothing is kept: a console
    /// that wants history keeps its own. An exception thrown by a handler is ignored. See
    /// <see cref="KissTrafficKind"/>.
    /// </summary>
    public event EventHandler<KissTrafficEventArgs>? Traffic;

    /// <summary>
    /// Whether someone is working at the TNC's command line by hand (<see cref="BeginManual"/>). While they are, a
    /// connection with a <c>TncSetup</c> doesn't reopen the port to switch a TNC sending text back into KISS, so it
    /// doesn't fight them; the <see cref="KissWarning.TextNotKiss"/> warning still fires.
    /// </summary>
    public bool IsManual => Volatile.Read(ref _manual) > 0;

    /// <summary>
    /// Starts working at the TNC's command line by hand, over this open connection, for a troubleshooting console:
    /// <see cref="SendRawAsync"/>, <see cref="SendLineAsync"/> and <see cref="RunScriptAsync"/> need it. Dispose the
    /// result to stop. Nesting is fine; manual mode lasts until the last one is disposed.
    /// </summary>
    /// <remarks>
    /// This is a setup and troubleshooting tool, not a way to send packets. Raw sends bypass
    /// <see cref="TransmitLimit"/>. In KISS mode a TNC ignores text, and only the KISS exit frame (<c>C0 FF C0</c>)
    /// reaches its command line; raw bytes that form a data frame are transmitted. At a TNC's command line some
    /// commands transmit too, such as <c>ID</c>, <c>CONV</c> and <c>K</c> on TNC-2-style TNCs.
    /// </remarks>
    public IDisposable BeginManual()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Interlocked.Increment(ref _manual);
        return new ManualMode(this);
    }

    /// <summary>
    /// Sends <paramref name="bytes"/> to the TNC as they are, outside any frame, over the open connection: written
    /// between frames, never inside one. Reported as <see cref="KissTrafficKind.RawSent"/>. See the warnings on
    /// <see cref="BeginManual"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not in manual mode, or the TNC is not connected now.</exception>
    public async Task SendRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        RequireManual();
        await Connected.SendRawAsync(bytes, KissTrafficKind.RawSent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends <paramref name="command"/> and a carriage return, as typed at the TNC's prompt; see <see cref="SendRawAsync"/>.</summary>
    /// <exception cref="InvalidOperationException">Not in manual mode, or the TNC is not connected now.</exception>
    /// <exception cref="ArgumentException">The command holds a character above U+00FF.</exception>
    public Task SendLineAsync(string command, CancellationToken cancellationToken = default) =>
        SendRawAsync(TncScriptStep.SendLine(command).Bytes, cancellationToken);

    /// <summary>
    /// Runs <paramref name="script"/> over the open connection, without reopening the port: its sends go out between
    /// frames, and its expect steps see the TNC's answers, which also come through <see cref="Traffic"/> as usual.
    /// Switching a TNC into KISS (such as <c>TncSetup.KenwoodThD72.EnterKiss</c>) or out of it becomes one action.
    /// Its sends are reported as <see cref="KissTrafficKind.SetupSent"/>, a failed step as
    /// <see cref="KissTrafficKind.SetupFailed"/>. Returns what the TNC said meanwhile. One script at a time.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Not in manual mode, the TNC is not connected now, or another script is running.
    /// </exception>
    /// <exception cref="TncScriptException">An expected answer didn't come in time.</exception>
    public async Task<string> RunScriptAsync(TncScript script, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        RequireManual();
        var client = Connected;
        if (Interlocked.Exchange(ref _scriptRunning, 1) != 0)
        {
            throw new InvalidOperationException("Another script is running on this connection.");
        }

        var feed = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
        client.ScriptFeed = feed.Writer;
        try
        {
            return await script
                .RunAsync(
                    new ScriptStream(client, feed.Reader),
                    _timeProvider,
                    (step, kind, bytes) =>
                    {
                        // What the TNC answers already comes through as Unframed; report what the script did.
                        if (kind != KissTrafficKind.SetupReceived && IsTracing)
                        {
                            RaiseTraffic(
                                new KissTrafficEventArgs(_timeProvider.GetUtcNow(), kind, bytes.ToArray())
                                {
                                    SetupStep = step,
                                }
                            );
                        }
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            client.ScriptFeed = null;
            feed.Writer.TryComplete();
            Volatile.Write(ref _scriptRunning, 0);
        }
    }

    private void RequireManual()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!IsManual)
        {
            throw new InvalidOperationException(
                "Sending to the TNC by hand needs manual mode: call BeginManual first. It is a setup and "
                    + "troubleshooting tool, not a way to send packets."
            );
        }
    }

    /// <summary>Ends manual mode once, on the first dispose.</summary>
    private sealed class ManualMode(KissConnection connection) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref connection._manual);
            }
        }
    }

    /// <summary>
    /// The open connection as a stream for a script: reads come from a copy of what the connection reads, writes go
    /// out between frames, and draining throws away what the script hasn't read. Nothing here closes the port.
    /// </summary>
    private sealed class ScriptStream(KissStreamClient client, System.Threading.Channels.ChannelReader<byte[]> feed)
        : Stream,
            IDiscardsInput
    {
        private byte[] _pending = [];
        private int _pendingAt;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_pendingAt == _pending.Length)
            {
                if (!await feed.WaitToReadAsync(cancellationToken).ConfigureAwait(false) || !feed.TryRead(out var next))
                {
                    return 0;
                }

                (_pending, _pendingAt) = (next, 0);
            }

            var count = Math.Min(buffer.Length, _pending.Length - _pendingAt);
            _pending.AsMemory(_pendingAt, count).CopyTo(buffer);
            _pendingAt += count;
            return count;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => client.SendRawAsync(buffer, kind: null, cancellationToken);

        public void DiscardInput()
        {
            (_pending, _pendingAt) = ([], 0);
            while (feed.TryRead(out _)) { }
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Whether anyone listens to <see cref="Traffic"/>.</summary>
    private protected bool IsTracing => Traffic is not null;

    /// <summary>Raises <see cref="Traffic"/>, ignoring an exception a handler throws so reading carries on.</summary>
    private protected void RaiseTraffic(KissTrafficEventArgs entry)
    {
        try
        {
            Traffic?.Invoke(this, entry);
        }
        catch (Exception)
        {
            // A console's bug must not stop the packets.
        }
    }

    /// <summary>An AGWPE server listed its radio ports; see <see cref="AgwpeConnection"/>.</summary>
    private protected virtual void AgwpePortsRead(IReadOnlyList<AgwpePort> ports) { }

    /// <summary>An AGWPE server gave its version; see <see cref="AgwpeConnection"/>.</summary>
    private protected virtual void AgwpeVersionRead(string version) { }

    /// <summary>The clock for traffic entries.</summary>
    private protected TimeProvider Clock => _timeProvider;

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
        await foreach (var heard in ReadPortFramesAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return heard.Frame;
        }
    }

    /// <summary>
    /// As <see cref="ReadFramesAsync"/>, with the radio port each frame was heard on: for a multi-port TNC read with
    /// <see cref="AllPorts"/>. Use one of the read methods at a time.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another read is already running.</exception>
    public async IAsyncEnumerable<KissPortFrame> ReadPortFramesAsync(
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
                using var sendingText = CancellationTokenSource.CreateLinkedTokenSource(token);
                try
                {
                    // This connection applies the limit itself, so it holds across reconnects.
                    client = new KissStreamClient(
                        await _connect(token).ConfigureAwait(false),
                        _tncPort,
                        transmitLimit: KissTransmitLimit.Off
                    )
                    {
                        AllPorts = AllPorts,
                        MetricsTnc = _tnc,
                        DataRead = DataArrived,
                        TimeProvider = _timeProvider,
                        WantsTraffic = () => IsTracing,
                        TextMode = _textMode,
                        Agwpe = _agwpe,
                        AgwpePortsRead = AgwpePortsRead,
                        AgwpeVersionRead = AgwpeVersionRead,
                    };
                    client.Traffic += (_, e) => RaiseTraffic(e);
                    client.NmeaSentenceReceived += (_, e) => SentenceArrived(e);
                    client.Warning += (_, e) =>
                    {
                        SetTextWarning(e.IsActive);
                        if (e.IsActive && _reopenWhenSendingText && !IsManual)
                        {
                            // A setup switches it into KISS each time the port opens: open it again.
                            sendingText.Cancel();
                        }
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
                    WatchQuiet(true);
                    await using (client.ConfigureAwait(false))
                    {
                        var frames = client
                            .ReadPortFramesAsync(sendingText.Token)
                            .GetAsyncEnumerator(sendingText.Token);
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
                                    when (sendingText.IsCancellationRequested && !token.IsCancellationRequested)
                                {
                                    failure = new IOException(
                                        "The TNC is sending text, not KISS frames; opening it again to switch it to KISS."
                                    );
                                    break;
                                }
                                catch (OperationCanceledException)
                                    when (IsDisposed && !cancellationToken.IsCancellationRequested)
                                {
                                    break;
                                }

                                SetTextWarning(false); // a frame came, so the TNC speaks KISS (again)
                                yield return frames.Current;
                            }
                        }
                        finally
                        {
                            WatchQuiet(false);
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
    public ValueTask SendFrameAsync(Ax25Frame frame, CancellationToken cancellationToken = default) =>
        SendFrameToPortAsync(TncPort, frame, cancellationToken);

    /// <summary>
    /// Sends <paramref name="packet"/> for the TNC to transmit on radio port <paramref name="port"/> of a multi-port TNC,
    /// once that port's transmit limit allows.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> isn't 0 to 15.</exception>
    /// <exception cref="InvalidOperationException">The TNC is not connected now; nothing was sent.</exception>
    /// <exception cref="ArgumentException">The packet cannot be sent as AX.25; see <see cref="Ax25Frame.FromAprsPacket"/>.</exception>
    public ValueTask SendToPortAsync(int port, AprsPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return SendFrameToPortAsync(port, Ax25Frame.FromAprsPacket(packet), cancellationToken);
    }

    /// <summary>
    /// Sends an AX.25 frame for the TNC to transmit on radio port <paramref name="port"/> of a multi-port TNC, once that
    /// port's transmit limit allows: <see cref="TransmitLimit"/> for <see cref="TncPort"/>, and one with the same
    /// settings for each other port.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> isn't 0 to 15.</exception>
    /// <exception cref="InvalidOperationException">
    /// The TNC is not connected now; nothing was sent. Checked before and after waiting for the limit.
    /// </exception>
    public async ValueTask SendFrameToPortAsync(
        int port,
        Ax25Frame frame,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 15);
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _ = Connected;
        var limit = port == TncPort ? TransmitLimit : _portLimits.GetOrAdd(port, _ => TransmitLimit.CreateLike());
        await limit.WaitAsync(cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await Connected.SendFrameToPortAsync(port, frame, cancellationToken).ConfigureAwait(false);
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
        await _quietTimer.DisposeAsync().ConfigureAwait(false);
        await _gpsTimer.DisposeAsync().ConfigureAwait(false);
        SetState(KissConnectionState.Closed);
        GC.SuppressFinalize(this);
    }

    // What a lost or unreachable TNC throws: a network error, a timeout, a serial port that went away (IOException),
    // or one another program holds or the user may not open.
    private static bool IsLinkFailure(Exception e) =>
        e is SocketException or IOException or TimeoutException or UnauthorizedAccessException;

    /// <summary>Starts timing silence when a connection is made, and stops when it ends.</summary>
    private void WatchQuiet(bool connected)
    {
        if (QuietWarning is not { } quiet)
        {
            return;
        }

        lock (_quietLock)
        {
            _watchingQuiet = connected;
            _lastData = _timeProvider.GetTimestamp();
            _quietTimer.Change(connected ? quiet : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void DataArrived()
    {
        if (QuietWarning is not { } quiet)
        {
            return;
        }

        bool wasQuiet;
        lock (_quietLock)
        {
            _lastData = _timeProvider.GetTimestamp();
            wasQuiet = _quiet;
            if (wasQuiet)
            {
                _quiet = false;
                _quietTimer.Change(quiet, Timeout.InfiniteTimeSpan);
            }
        }

        if (wasQuiet)
        {
            Warning?.Invoke(this, new KissWarningEventArgs(KissWarning.Quiet, false));
        }
    }

    /// <summary>The timer: warns if nothing came for <see cref="QuietWarning"/>, or waits out the rest of it.</summary>
    private void CheckQuiet()
    {
        var quiet = QuietWarning!.Value;
        bool warn;
        lock (_quietLock)
        {
            if (!_watchingQuiet || _quiet || IsDisposed)
            {
                return;
            }

            var silent = _timeProvider.GetElapsedTime(_lastData);
            warn = silent >= quiet;
            if (warn)
            {
                _quiet = true; // stays set across reconnects until bytes arrive
            }
            else
            {
                _quietTimer.Change(quiet - silent, Timeout.InfiniteTimeSpan);
            }
        }

        if (warn)
        {
            Warning?.Invoke(this, new KissWarningEventArgs(KissWarning.Quiet, true, quiet));
        }
    }

    /// <summary>A GPS sentence: passes it on, and says the GPS is there if it wasn't.</summary>
    private void SentenceArrived(NmeaSentenceEventArgs sentence)
    {
        bool appeared;
        lock (_gpsLock)
        {
            _lastSentence = _timeProvider.GetTimestamp();
            appeared = !_gpsPresent && !IsDisposed;
            if (appeared)
            {
                _gpsPresent = true;
                _gpsTimer.Change(GpsTimeout, Timeout.InfiniteTimeSpan);
            }
        }

        if (appeared)
        {
            RaiseIgnoringErrors(GpsPresenceChanged, new GpsPresenceChangedEventArgs(true, GpsTimeout));
        }

        RaiseIgnoringErrors(NmeaSentenceReceived, sentence);
    }

    /// <summary>The timer: says the GPS is gone if no sentence came for <see cref="GpsTimeout"/>, or waits out the rest.</summary>
    private void CheckGps()
    {
        bool gone;
        lock (_gpsLock)
        {
            if (!_gpsPresent || IsDisposed)
            {
                return;
            }

            var silent = _timeProvider.GetElapsedTime(_lastSentence);
            gone = silent >= GpsTimeout;
            if (gone)
            {
                _gpsPresent = false;
            }
            else
            {
                _gpsTimer.Change(GpsTimeout - silent, Timeout.InfiniteTimeSpan);
            }
        }

        if (gone)
        {
            RaiseIgnoringErrors(GpsPresenceChanged, new GpsPresenceChangedEventArgs(false, GpsTimeout));
        }
    }

    private void RaiseIgnoringErrors<T>(EventHandler<T>? handler, T args)
    {
        try
        {
            handler?.Invoke(this, args);
        }
        catch (Exception)
        {
            // An app's bug must not stop the packets.
        }
    }

    private void SetTextWarning(bool active)
    {
        if (Interlocked.Exchange(ref _textWarning, active ? 1 : 0) != (active ? 1 : 0))
        {
            Warning?.Invoke(this, new KissWarningEventArgs(KissWarning.TextNotKiss, active));
        }
    }

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
