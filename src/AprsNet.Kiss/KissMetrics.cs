using System.Diagnostics.Metrics;

namespace AprsNet.Kiss;

/// <summary>
/// Instruments for KISS clients and <see cref="KissTcpServer"/>, on the <c>AprsNet.Kiss</c> meter. Every client
/// measurement carries a <c>tnc</c> tag: the TNC's <c>host:port</c> for TCP, the port name for a serial connection, or
/// <c>stream</c> for a <see cref="KissStreamClient"/> on a stream the app opened. Every server measurement carries a
/// <c>server</c> tag: the address it listens on.
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

    private static readonly Counter<long> _discarded = _meter.CreateCounter<long>(
        "aprsnet.kiss.bytes.discarded",
        "By",
        "Bytes from the TNC that were not part of a frame passed on: noise, or text from a TNC not in KISS mode."
    );

    private static readonly Counter<long> _nmeaReceived = _meter.CreateCounter<long>(
        "aprsnet.kiss.nmea.received",
        "{sentence}",
        "GPS sentences a TNC in text mode printed between its packet lines, whole with a checksum that matches."
    );

    private static readonly Counter<long> _nmeaDamaged = _meter.CreateCounter<long>(
        "aprsnet.kiss.nmea.damaged",
        "{sentence}",
        "Lines starting with $ from a TNC in text mode that weren't whole GPS sentences: cut off, or a wrong checksum."
    );

    private static readonly UpDownCounter<long> _active = _meter.CreateUpDownCounter<long>(
        "aprsnet.kiss.connection.active",
        "{connection}",
        "Connections to TNCs open now, over TCP or a serial port."
    );

    private static readonly Counter<long> _reconnects = _meter.CreateCounter<long>(
        "aprsnet.kiss.connection.reconnects",
        "{reconnect}",
        "Waits before reconnecting, by cause: tnc_closed, socket_error or connect_failed, as for APRS-IS."
    );

    private static readonly UpDownCounter<long> _serverClients = _meter.CreateUpDownCounter<long>(
        "aprsnet.kiss.server.clients.connected",
        "{client}",
        "Apps connected to a KISS TCP server now."
    );

    private static readonly Counter<long> _serverDisconnected = _meter.CreateCounter<long>(
        "aprsnet.kiss.server.clients.disconnected",
        "{client}",
        "Apps that left a KISS TCP server or were turned away, by reason: closed, overflow, server_stopping or "
            + "server_full."
    );

    private static readonly Counter<long> _serverPublished = _meter.CreateCounter<long>(
        "aprsnet.kiss.server.frames.published",
        "{frame}",
        "Frames a KISS TCP server sent to its apps; one frame counts once however many apps it went to."
    );

    private static readonly Counter<long> _serverFromClients = _meter.CreateCounter<long>(
        "aprsnet.kiss.server.frames.received",
        "{frame}",
        "AX.25 frames apps sent to a KISS TCP server."
    );

    private static readonly Counter<long> _serverTransmitFailures = _meter.CreateCounter<long>(
        "aprsnet.kiss.server.frames.transmit_failures",
        "{frame}",
        "Frames from apps that a KISS TCP server could not pass on, such as while the TNC was not connected."
    );

    public static void Received(string tnc, int port) => _received.Add(1, Tnc(tnc), Port(port));

    public static void Sent(string tnc, int port) => _sent.Add(1, Tnc(tnc), Port(port));

    public static void Dropped(string tnc, long count) => _dropped.Add(count, Tnc(tnc));

    public static void Discarded(string tnc, long count) => _discarded.Add(count, Tnc(tnc));

    public static void NmeaReceived(string tnc) => _nmeaReceived.Add(1, Tnc(tnc));

    public static void NmeaDamaged(string tnc) => _nmeaDamaged.Add(1, Tnc(tnc));

    public static void Active(string tnc, int change) => _active.Add(change, Tnc(tnc));

    public static void Reconnect(string tnc, string cause) =>
        _reconnects.Add(1, Tnc(tnc), new KeyValuePair<string, object?>("cause", cause));

    public static void ServerClients(string server, int change) => _serverClients.Add(change, Server(server));

    public static void ServerDisconnected(string server, string reason) =>
        _serverDisconnected.Add(1, Server(server), new KeyValuePair<string, object?>("reason", reason));

    public static void ServerPublished(string server) => _serverPublished.Add(1, Server(server));

    public static void ServerFromClients(string server) => _serverFromClients.Add(1, Server(server));

    public static void ServerTransmitFailure(string server) => _serverTransmitFailures.Add(1, Server(server));

    private static KeyValuePair<string, object?> Tnc(string tnc) => new("tnc", tnc);

    // The radio port as text, so a backend groups it as a label: "0" to "15".
    private static KeyValuePair<string, object?> Port(int port) =>
        new("port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static KeyValuePair<string, object?> Server(string server) => new("server", server);
}
