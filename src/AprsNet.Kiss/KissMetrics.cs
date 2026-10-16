using System.Diagnostics.Metrics;

namespace AprsNet.Kiss;

/// <summary>
/// Instruments for KISS clients, on the <c>AprsNet.Kiss</c> meter. Every measurement carries a <c>tnc</c> tag: the TNC's
/// <c>host:port</c> for TCP, or <c>stream</c> for a <see cref="KissStreamClient"/> on a stream the app opened.
/// </summary>
internal static class KissMetrics
{
    public const string MeterName = "AprsNet.Kiss";

    /// <summary>The <c>tnc</c> tag of a client on a stream the app opened.</summary>
    public const string StreamTnc = "stream";

    private static readonly Meter _meter = new(MeterName);

    private static readonly Counter<long> _received = _meter.CreateCounter<long>(
        "aprsnet.kiss.frames.received",
        "{frame}",
        "AX.25 frames read from the TNC on the client's radio port."
    );

    private static readonly Counter<long> _sent = _meter.CreateCounter<long>(
        "aprsnet.kiss.frames.sent",
        "{frame}",
        "Frames sent to the TNC to transmit."
    );

    private static readonly Counter<long> _dropped = _meter.CreateCounter<long>(
        "aprsnet.kiss.frames.dropped",
        "{frame}",
        "KISS frames dropped for an invalid escape or for being too long: a sign of a noisy link."
    );

    private static readonly UpDownCounter<long> _active = _meter.CreateUpDownCounter<long>(
        "aprsnet.kiss.connection.active",
        "{connection}",
        "TCP connections to TNCs open now."
    );

    private static readonly Counter<long> _reconnects = _meter.CreateCounter<long>(
        "aprsnet.kiss.connection.reconnects",
        "{reconnect}",
        "Waits before reconnecting, by cause: closed (the TNC closed the connection), error or connect_failed."
    );

    public static void Received(string tnc) => _received.Add(1, Tnc(tnc));

    public static void Sent(string tnc) => _sent.Add(1, Tnc(tnc));

    public static void Dropped(string tnc, long count) => _dropped.Add(count, Tnc(tnc));

    public static void Active(string tnc, int change) => _active.Add(change, Tnc(tnc));

    public static void Reconnect(string tnc, string cause) =>
        _reconnects.Add(1, Tnc(tnc), new KeyValuePair<string, object?>("cause", cause));

    private static KeyValuePair<string, object?> Tnc(string tnc) => new("tnc", tnc);
}
