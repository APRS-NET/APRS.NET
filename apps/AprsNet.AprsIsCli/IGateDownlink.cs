using System.Threading.Channels;
using AprsNet.Gateway;
using AprsNet.IGate;
using AprsNet.Kiss;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The internet-to-RF side of <c>serve --igate-to-rf</c>: records the stations the TNC hears, and sends messages from
/// APRS-IS for the local ones on RF through the TNC. Packets to send wait in a small queue behind the TNC's transmit
/// limit; when it is full the newest are dropped and counted, since a message sent late is retried by its sender.
/// </summary>
internal sealed partial class IGateDownlink
{
    private const int QueueCapacity = 10;

    private readonly HeardStations _heard;
    private readonly InternetToRfGate _gate;
    private readonly ILogger _logger;
    private readonly Channel<AprsPacket> _queue = Channel.CreateBounded<AprsPacket>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait }
    );
    private readonly long[] _refused = new long[Enum.GetValues<RfGateRefusal>().Length];
    private long _gated;
    private long _sent;
    private long _dropped;

    public IGateDownlink(PacketAddress igate, TimeProvider timeProvider, ILogger logger)
    {
        _heard = new HeardStations(
            new HeardStationsOptions { MaxLocalHops = ServeCommand.Hops(igate.Path), TimeProvider = timeProvider }
        );
        _gate = new InternetToRfGate(igate, _heard, new InternetToRfGateOptions { TimeProvider = timeProvider });
        _logger = logger;
    }

    /// <summary>The TNC to transmit on; set before <see cref="RunAsync"/>.</summary>
    public KissConnection? Tnc { get; set; }

    /// <summary>How many hops a station may be heard over to count as local: the hops of the IGate's path.</summary>
    public int MaxLocalHops => _heard.MaxLocalHops;

    /// <summary>Packets from APRS-IS the gate passed to RF.</summary>
    public long Gated => Interlocked.Read(ref _gated);

    /// <summary>Gated packets the TNC took.</summary>
    public long Sent => Interlocked.Read(ref _sent);

    /// <summary>Gated packets lost: the queue was full, or the TNC couldn't take them.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Packets refused for <paramref name="reason"/>.</summary>
    public long Refused(RfGateRefusal reason) => Interlocked.Read(ref _refused[(int)reason]);

    /// <summary>
    /// Every refusal reason with a count above zero, in the enum's order, leaving out
    /// <see cref="RfGateRefusal.NotForAStation"/>: the rest of the feed, which never goes to RF.
    /// </summary>
    public IEnumerable<(RfGateRefusal Reason, long Count)> RefusedCounts() =>
        Enum.GetValues<RfGateRefusal>()
            .Where(reason => reason != RfGateRefusal.NotForAStation)
            .Select(reason => (reason, Refused(reason)))
            .Where(r => r.Item2 > 0);

    /// <summary>Reports the counts on <c>/health</c> as <c>igate.rf.gated</c>, <c>igate.rf.refused.not_local</c> and so on.</summary>
    public void AddTo(GatewayHealth health)
    {
        health.AddCounter("igate.rf.gated", () => Gated);
        health.AddCounter("igate.rf.sent", () => Sent);
        health.AddCounter("igate.rf.dropped", () => Dropped);
        health.AddCounter("igate.rf.local_stations", () => _heard.Stations.Count(s => _heard.IsLocal(s.Station)));
        foreach (var reason in Enum.GetValues<RfGateRefusal>())
        {
            health.AddCounter($"igate.rf.refused.{ReasonName(reason)}", () => Refused(reason));
        }
    }

    /// <summary><see cref="RfGateRefusal.NotLocal"/> → <c>not_local</c>.</summary>
    public static string ReasonName(RfGateRefusal reason) =>
        string.Concat(
            reason
                .ToString()
                .Select(
                    (c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c) + ""
                )
        );

    /// <summary>Passes on every packet the TNC hears, unchanged, recording who was heard.</summary>
    public async IAsyncEnumerable<AprsPacket> HearAsync(IAsyncEnumerable<AprsPacket> heard)
    {
        await foreach (var packet in heard.ConfigureAwait(false))
        {
            _heard.Record(packet);
            yield return packet;
        }
    }

    /// <summary>Passes on every packet from APRS-IS, unchanged, queueing the ones the gate passes for RF.</summary>
    public async IAsyncEnumerable<AprsPacket> GateAsync(IAsyncEnumerable<AprsPacket> fromInternet)
    {
        await foreach (var packet in fromInternet.ConfigureAwait(false))
        {
            var decision = _gate.Gate(packet);
            if (decision.IsGated)
            {
                Interlocked.Increment(ref _gated);
                ToRf(_logger, decision.Packet);
                if (!_queue.Writer.TryWrite(decision.Packet))
                {
                    Interlocked.Increment(ref _dropped);
                    QueueFull(_logger, decision.Packet);
                }
            }
            else
            {
                Interlocked.Increment(ref _refused[(int)decision.Refusal!.Value]);
                if (decision.Refusal != RfGateRefusal.NotForAStation)
                {
                    NotGated(_logger, decision.Refusal.Value, packet);
                }
            }

            yield return packet;
        }
    }

    /// <summary>Sends the queue to the TNC, until <paramref name="cancellationToken"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var packet in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await Tnc!.SendAsync(packet, cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref _sent);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref _dropped);
                    SendFailed(_logger, packet, ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Gating to RF: {Packet}")]
    private static partial void ToRf(ILogger logger, AprsPacket packet);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RF queue full; dropped {Packet}")]
    private static partial void QueueFull(ILogger logger, AprsPacket packet);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Not gated to RF ({Reason}): {Packet}")]
    private static partial void NotGated(ILogger logger, RfGateRefusal reason, AprsPacket packet);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TNC couldn't send {Packet}: {Reason}")]
    private static partial void SendFailed(ILogger logger, AprsPacket packet, string reason);
}
