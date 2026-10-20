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
/// Gated packets wait in a bounded queue, so a slow or reconnecting APRS-IS link never holds up the local feed; when
/// the queue is full the newest are dropped and counted.
/// </summary>
internal sealed partial class IGateUplink
{
    /// <summary>Packets waiting for APRS-IS: a few minutes of a busy 1200-baud channel.</summary>
    private const int QueueCapacity = 500;

    private readonly AprsIsConnection _connection;
    private readonly RfToInternetGate _gate;
    private readonly AprsPacket _beacon;
    private readonly TimeSpan _beaconInterval;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Channel<AprsPacket> _queue = Channel.CreateBounded<AprsPacket>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait }
    );
    private readonly long[] _refused = new long[Enum.GetValues<GateRefusal>().Length];
    private long _gated;
    private long _sent;
    private long _dropped;
    private long _beacons;

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

    /// <summary>Gated packets lost: the queue was full, or APRS-IS refused the line.</summary>
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

    /// <summary>Passes on every packet a TNC hears, unchanged, queueing the ones the gate passes for APRS-IS.</summary>
    public async IAsyncEnumerable<AprsPacket> TapAsync(IAsyncEnumerable<AprsPacket> heard)
    {
        await foreach (var packet in heard.ConfigureAwait(false))
        {
            var decision = _gate.Gate(packet);
            if (decision.IsGated)
            {
                Interlocked.Increment(ref _gated);
                if (!_queue.Writer.TryWrite(decision.Packet))
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
            await foreach (var packet in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // Waits while APRS-IS reconnects; the queue holds what arrives meanwhile.
                if (await TrySendAsync(packet, cancellationToken).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref _sent);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "IGate queue full; dropped {Packet}")]
    private static partial void QueueFull(ILogger logger, AprsPacket packet);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Not gated ({Reason}): {Packet}")]
    private static partial void NotGated(ILogger logger, GateRefusal reason, AprsPacket packet);

    [LoggerMessage(Level = LogLevel.Warning, Message = "APRS-IS refused {Packet}: {Reason}")]
    private static partial void SendRefused(ILogger logger, AprsPacket packet, string reason);
}
