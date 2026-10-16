using System.Diagnostics.Metrics;

namespace AprsNet.Gateway;

/// <summary>
/// Instruments for the gateway, on the <c>AprsNet.Gateway</c> meter. Every measurement carries the server's name as a
/// <c>server</c> tag (one value per server); client measurements also carry a <c>protocol</c> tag, <c>aprs-is</c> or
/// <c>websocket</c>. Other tag values are fixed strings to keep cardinality bounded.
/// </summary>
internal static class GatewayMetrics
{
    public const string MeterName = "AprsNet.Gateway";

    /// <summary>The <c>protocol</c> tag of APRS-IS clients.</summary>
    public const string AprsIs = "aprs-is";

    /// <summary>The <c>protocol</c> tag of WebSocket clients.</summary>
    public const string WebSocket = "websocket";

    private static readonly Meter _meter = new(MeterName);

    private static readonly UpDownCounter<long> _connected = _meter.CreateUpDownCounter<long>(
        "aprsnet.gateway.clients.connected",
        "{client}",
        "Clients connected now, including any still logging in."
    );

    private static readonly Counter<long> _logins = _meter.CreateCounter<long>(
        "aprsnet.gateway.logins",
        "{login}",
        "Client logins, by whether the passcode verified them."
    );

    private static readonly Counter<long> _disconnects = _meter.CreateCounter<long>(
        "aprsnet.gateway.clients.disconnected",
        "{client}",
        "Clients disconnected or refused, by reason."
    );

    private static readonly Counter<long> _published = _meter.CreateCounter<long>(
        "aprsnet.gateway.packets.published",
        "{packet}",
        "Packets given to the server to send."
    );

    private static readonly Counter<long> _queued = _meter.CreateCounter<long>(
        "aprsnet.gateway.packets.queued",
        "{packet}",
        "Packets queued for a client because its filter passed them; one packet may be queued for many clients."
    );

    private static readonly Counter<long> _duplicates = _meter.CreateCounter<long>(
        "aprsnet.gateway.packets.duplicates",
        "{packet}",
        "Packets a GatewayFeed dropped as duplicates, by source."
    );

    private static readonly Counter<long> _received = _meter.CreateCounter<long>(
        "aprsnet.gateway.packets.received",
        "{packet}",
        "Packets a GatewayFeed read, by source, duplicates included."
    );

    public static void Connected(string server, string protocol = AprsIs) =>
        _connected.Add(1, Server(server), Protocol(protocol));

    /// <summary>
    /// <paramref name="reason"/> is one of <c>closed</c> (the client left or the connection failed),
    /// <c>invalid_login</c>, <c>login_timeout</c>, <c>overflow</c>, <c>server_stopping</c>, <c>server_full</c> or
    /// <c>error</c> (an unexpected fault in that session); for WebSocket clients also <c>invalid_message</c>.
    /// </summary>
    public static void Disconnected(string server, string reason, bool wasConnected, string protocol = AprsIs)
    {
        if (wasConnected)
        {
            _connected.Add(-1, Server(server), Protocol(protocol));
        }

        _disconnects.Add(1, Server(server), new KeyValuePair<string, object?>("reason", reason), Protocol(protocol));
    }

    public static void Login(string server, bool verified) =>
        _logins.Add(1, Server(server), new KeyValuePair<string, object?>("verified", verified ? "true" : "false"));

    public static void Published(string server) => _published.Add(1, Server(server));

    public static void Queued(string server, string protocol = AprsIs) =>
        _queued.Add(1, Server(server), Protocol(protocol));

    public static void Duplicate(string server, string source) => _duplicates.Add(1, Server(server), Source(source));

    public static void Received(string server, string source) => _received.Add(1, Server(server), Source(source));

    private static KeyValuePair<string, object?> Server(string server) => new("server", server);

    private static KeyValuePair<string, object?> Source(string source) => new("source", source);

    private static KeyValuePair<string, object?> Protocol(string protocol) => new("protocol", protocol);
}
