using System.Threading.Channels;
using AprsNet.AprsIsTcpClient;
using AprsNet.Beacons;
using AprsNet.Gateway;
using AprsNet.IGate;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The IGate side of <c>serve --igate</c>: gates what the TNCs hear to APRS-IS, and beacons the IGate's position there.
/// Gated packets wait in a bounded queue, so a short burst never holds up the local feed; when the queue is full the
/// newest are dropped and counted.
/// </summary>
/// <remarks>
/// Nothing is saved up while APRS-IS is down, as aprsc's IGate hints ask: a packet heard then is dropped at once, and
/// a queued packet that could not be sent within <see cref="MaxQueueWait"/> is dropped rather than sent late. Another
/// IGate has probably gated it already, and a late copy, outside APRS-IS's 30 s duplicate window, would make a moving
/// station jump back on the maps. See https://github.com/hessu/aprsc/blob/main/doc/IGATE-HINTS.md.
/// </remarks>
internal sealed partial class IGateUplink
{
    /// <summary>Packets waiting for APRS-IS: a few minutes of a busy 1200-baud channel.</summary>
    private const int QueueCapacity = 500;

    /// <summary>The longest a gated packet may wait to be sent: well inside APRS-IS's 30 s duplicate window.</summary>
    internal static readonly TimeSpan MaxQueueWait = TimeSpan.FromSeconds(10);

    private readonly AprsIsConnection _connection;
    private readonly RfToInternetGate _gate;
    private readonly AprsPacket _beacon;
    private readonly TimeSpan _beaconInterval;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Channel<Queued> _queue = Channel.CreateBounded<Queued>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait }
    );
    private readonly long[] _refused = new long[Enum.GetValues<GateRefusal>().Length];
    private long _gated;
    private long _sent;
    private long _dropped;
    private long _beacons;
    private long _droppedWhileDown;
    private int _down;
    private int _late;

    public IGateUplink(
        AprsIsConnection connection,
        RfToInternetGate gate,
        AprsPacket beacon,
        TimeSpan beaconInterval,
        TimeProvider timeProvider,
        ILogger logger
    )
    {
        _connection = connection;
        _gate = gate;
        _beacon = beacon;
        _beaconInterval = beaconInterval;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Packets the gate passed to APRS-IS.</summary>
    public long Gated => Interlocked.Read(ref _gated);

    /// <summary>Gated packets written to APRS-IS.</summary>
    public long Sent => Interlocked.Read(ref _sent);

    /// <summary>
    /// Gated packets lost: APRS-IS was down, the packet waited too long to be sent, the queue was full, or APRS-IS
    /// refused the line.
    /// </summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Position beacons sent to APRS-IS.</summary>
    public long Beacons => Interlocked.Read(ref _beacons);

    /// <summary>Packets refused for <paramref name="reason"/>.</summary>
    public long Refused(GateRefusal reason) => Interlocked.Read(ref _refused[(int)reason]);

    /// <summary>Every refusal reason with a count above zero, in the enum's order.</summary>
    public IEnumerable<(GateRefusal Reason, long Count)> RefusedCounts() =>
        Enum.GetValues<GateRefusal>().Select(reason => (reason, Refused(reason))).Where(r => r.Item2 > 0);

    /// <summary>Reports the counts on <c>/health</c> as <c>igate.gated</c>, <c>igate.refused.duplicate</c> and so on.</summary>
    public void AddTo(GatewayHealth health)
    {
        health.AddCounter("igate.gated", () => Gated);
        health.AddCounter("igate.sent", () => Sent);
        health.AddCounter("igate.dropped", () => Dropped);
        health.AddCounter("igate.beacons", () => Beacons);
        foreach (var reason in Enum.GetValues<GateRefusal>())
        {
            health.AddCounter($"igate.refused.{ReasonName(reason)}", () => Refused(reason));
        }
    }

    /// <summary><see cref="GateRefusal.FromInternet"/> → <c>from_internet</c>, as the metric tags spell it.</summary>
    public static string ReasonName(GateRefusal reason) =>
        string.Concat(
            reason
                .ToString()
                .Select(
                    (c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c) + ""
                )
        );

    /// <summary>
    /// Passes on every packet a TNC hears, unchanged, queueing the ones the gate passes for APRS-IS; while APRS-IS is
    /// down they are dropped instead.
    /// </summary>
    public async IAsyncEnumerable<AprsPacket> TapAsync(IAsyncEnumerable<AprsPacket> heard)
    {
        await foreach (var packet in heard.ConfigureAwait(false))
        {
            var decision = _gate.Gate(packet);
            if (decision.IsGated)
            {
                Interlocked.Increment(ref _gated);
                if (!IsUp)
                {
                    DropWhileDown(decision.Packet);
                }
                else if (!_queue.Writer.TryWrite(new Queued(decision.Packet, _timeProvider.GetTimestamp())))
                {
                    Interlocked.Increment(ref _dropped);
                    QueueFull(_logger, decision.Packet);
                }
            }
            else
            {
                Interlocked.Increment(ref _refused[(int)decision.Refusal!.Value]);
                NotGated(_logger, decision.Refusal.Value, packet);
            }

            yield return packet;
        }
    }

    /// <summary>Sends the queue to APRS-IS and beacons, until <paramref name="cancellationToken"/> is cancelled.</summary>
    public Task RunAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(SendQueueAsync(cancellationToken), BeaconAsync(cancellationToken));

    private async Task SendQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                var (packet, queuedAt) in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)
            )
            {
                // Never wait for a reconnect: what can't go out now would only go out late.
                if (!IsUp)
                {
                    DropWhileDown(packet);
                    continue;
                }

                var left = MaxQueueWait - _timeProvider.GetElapsedTime(queuedAt);
                if (left <= TimeSpan.Zero)
                {
                    DropLate(packet);
                    continue;
                }

                // The connection can drop after the check above: give up when the packet's time is up.
                using var deadline = new CancellationTokenSource(left, _timeProvider);
                using var send = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                bool sent;
                try
                {
                    sent = await TrySendAsync(packet, send.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    DropLate(packet);
                    continue;
                }

                if (sent)
                {
                    Interlocked.Increment(ref _sent);
                    Recovered();
                }
                else
                {
                    Interlocked.Increment(ref _dropped);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task BeaconAsync(CancellationToken cancellationToken)
    {
        // An APRS-IS beacon costs no airtime, so the first goes at once.
        var schedule = new FixedBeaconSchedule(_beaconInterval, TimeSpan.Zero, _timeProvider);
        try
        {
            while (true)
            {
                await Task.Delay(schedule.DueIn, _timeProvider, cancellationToken).ConfigureAwait(false);
                if (await TrySendAsync(_beacon, cancellationToken).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref _beacons);
                }

                schedule.Sent();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    /// <summary>Logged in and verified now, so a packet sent now goes out now.</summary>
    private bool IsUp => _connection.Login is { Status: LoginStatus.Verified };

    private void DropWhileDown(AprsPacket packet)
    {
        Interlocked.Increment(ref _dropped);
        Interlocked.Increment(ref _droppedWhileDown);
        if (Interlocked.Exchange(ref _down, 1) == 0)
        {
            AprsIsDown(_logger);
        }

        DroppedPacket(_logger, "APRS-IS is down", packet);
    }

    private void DropLate(AprsPacket packet)
    {
        Interlocked.Increment(ref _dropped);
        if (Interlocked.Exchange(ref _late, 1) == 0)
        {
            SendingLate(_logger, MaxQueueWait.TotalSeconds);
        }

        DroppedPacket(_logger, "waited too long", packet);
    }

    /// <summary>A packet went out: say so once if packets were being dropped.</summary>
    private void Recovered()
    {
        if (Interlocked.Exchange(ref _down, 0) == 1)
        {
            var dropped = Interlocked.Exchange(ref _droppedWhileDown, 0);
            AprsIsBack(_logger, dropped);
        }

        Interlocked.Exchange(ref _late, 0);
    }

    private async Task<bool> TrySendAsync(AprsPacket packet, CancellationToken cancellationToken)
    {
        try
        {
            await _connection.SendAsync(packet, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            SendRefused(_logger, packet, ex.Message);
            return false;
        }
    }

    // Packets, and the reasons given for them, are logged as text escaped for the console: the log goes to stderr, and
    // a packet heard on RF could otherwise drive the terminal.
    private static void QueueFull(ILogger logger, AprsPacket packet)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            var line = Escaped(packet);
            QueueFull(logger, line);
        }
    }

    private static void DroppedPacket(ILogger logger, string reason, AprsPacket packet)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var line = Escaped(packet);
            DroppedPacket(logger, reason, line);
        }
    }

    private static void NotGated(ILogger logger, GateRefusal reason, AprsPacket packet)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var line = Escaped(packet);
            NotGated(logger, reason, line);
        }
    }

    private static void SendRefused(ILogger logger, AprsPacket packet, string reason)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            SendRefused(logger, Escaped(packet), LineFormatter.EscapeForConsole(reason));
        }
    }

    private static string Escaped(AprsPacket packet) => LineFormatter.EscapeForConsole(packet.ToString());

    [LoggerMessage(Level = LogLevel.Warning, Message = "IGate queue full; dropped {Packet}")]
    private static partial void QueueFull(ILogger logger, string packet);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "APRS-IS is down; dropping gated packets until it is back, not sending them late"
    )]
    private static partial void AprsIsDown(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "APRS-IS is back; gating again ({Dropped} dropped meanwhile)"
    )]
    private static partial void AprsIsBack(ILogger logger, long dropped);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "APRS-IS is slow; dropping gated packets that waited over {Seconds}s rather than sending them late"
    )]
    private static partial void SendingLate(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped ({Reason}): {Packet}")]
    private static partial void DroppedPacket(ILogger logger, string reason, string packet);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Not gated ({Reason}): {Packet}")]
    private static partial void NotGated(ILogger logger, GateRefusal reason, string packet);

    [LoggerMessage(Level = LogLevel.Warning, Message = "APRS-IS refused {Packet}: {Reason}")]
    private static partial void SendRefused(ILogger logger, string packet, string reason);

    /// <summary>A gated packet and when it was queued, as a <see cref="TimeProvider"/> timestamp.</summary>
    private readonly record struct Queued(AprsPacket Packet, long QueuedAt);
}
