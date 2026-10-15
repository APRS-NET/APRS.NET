using System.Diagnostics.Metrics;

namespace AprsNet.AprsIsTcpClient;

/// <summary>Instruments for the client; see the metrics plan. Tag values are fixed strings to keep cardinality bounded.</summary>
internal static class ClientMetrics
{
    private const string _meterName = "AprsNet.AprsIsTcpClient";

    private static readonly Meter _meter = new(_meterName);

    private static readonly Counter<long> _linesReceived = _meter.CreateCounter<long>(
        "aprsnet.aprsis.lines.received",
        "{line}",
        "Lines read from the server."
    );

    private static readonly Counter<long> _connectionAttempts = _meter.CreateCounter<long>(
        "aprsnet.aprsis.connection.attempts",
        "{attempt}",
        "Connection and login attempts."
    );

    private static readonly Counter<long> _loginResults = _meter.CreateCounter<long>(
        "aprsnet.aprsis.login.results",
        "{login}",
        "Server login responses."
    );

    private static readonly Counter<long> _dropped = _meter.CreateCounter<long>(
        "aprsnet.aprsis.packets.dropped",
        "{packet}",
        "Lines discarded before delivery."
    );

    private static readonly Counter<long> _packetsReceived = _meter.CreateCounter<long>(
        "aprsnet.aprsis.packets.received",
        "{packet}",
        "Packets parsed from the server's lines, by type and outcome."
    );

    /// <summary>One packet parsed by <c>ReadPacketsAsync</c>. Both tags come from fixed enums, so they stay bounded.</summary>
    public static void PacketParsed(Packets.PacketType type, Packets.ParseOutcome outcome)
    {
        _packetsReceived.Add(
            1,
            new KeyValuePair<string, object?>("type", Snake(type.ToString())),
            new KeyValuePair<string, object?>("outcome", Snake(outcome.ToString()))
        );
    }

    private static readonly Counter<long> _addressedToUser = _meter.CreateCounter<long>(
        "aprsnet.aprsis.packets.addressed_to_user",
        "{packet}",
        "Messages, acknowledgements and rejections addressed to the login callsign, read with ReadPacketsAsync."
    );

    private static readonly UpDownCounter<long> _activeConnections = _meter.CreateUpDownCounter<long>(
        "aprsnet.aprsis.connection.active",
        "{connection}",
        "Connections logged in and reading; 1 while one client is connected."
    );

    private static readonly Counter<long> _bytesSent = _meter.CreateCounter<long>(
        "aprsnet.aprsis.bytes.sent",
        "By",
        "Bytes written to the server, including the login, filter commands and line endings."
    );

    /// <summary>A message, ack or rej addressed to the login callsign; <paramref name="kind"/> is message, ack or rej.</summary>
    public static void AddressedToUser(string kind)
    {
        _addressedToUser.Add(1, new KeyValuePair<string, object?>("kind", kind));
    }

    /// <summary>A connection logged in (+1) or ended (-1).</summary>
    public static void ConnectionActive(int change)
    {
        _activeConnections.Add(change);
    }

    public static void BytesSent(int count)
    {
        _bytesSent.Add(count);
    }

    /// <summary><c>MicE</c> → <c>mic_e</c>, <c>ThirdParty</c> → <c>third_party</c>.</summary>
    private static string Snake(string name) =>
        string.Concat(
            name.Select(
                (c, i) =>
                    i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()
            )
        );

    public static void Packet()
    {
        _linesReceived.Add(1, new KeyValuePair<string, object?>("kind", "packet"));
    }

    public static void ServerComment()
    {
        _linesReceived.Add(1, new KeyValuePair<string, object?>("kind", "server_comment"));
    }

    private static readonly Counter<long> _idleTimeouts = _meter.CreateCounter<long>(
        "aprsnet.aprsis.connection.idle_timeouts",
        "{timeout}",
        "Connections ended because the server sent nothing within the idle timeout."
    );

    public static void IdleTimeout()
    {
        _idleTimeouts.Add(1);
    }

    private static readonly Counter<long> _packetsSent = _meter.CreateCounter<long>(
        "aprsnet.aprsis.packets.sent",
        "{packet}",
        "Packets written to the server."
    );

    public static void PacketSent()
    {
        _packetsSent.Add(1);
    }

    private static readonly Counter<long> _reconnects = _meter.CreateCounter<long>(
        "aprsnet.aprsis.connection.reconnects",
        "{reconnect}",
        "Automatic reconnects by AprsIsConnection, by what ended the previous connection."
    );

    /// <summary>
    /// <paramref name="cause"/> is one of <c>server_closed</c>, <c>socket_error</c>, <c>keepalive_timeout</c>,
    /// <c>packet_silence</c> or <c>connect_failed</c>.
    /// </summary>
    public static void Reconnect(string cause)
    {
        _reconnects.Add(1, new KeyValuePair<string, object?>("cause", cause));
    }

    public static void Attempt(string outcome)
    {
        _connectionAttempts.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void Login(LoginStatus status)
    {
        _loginResults.Add(
            1,
            new KeyValuePair<string, object?>("result", status == LoginStatus.Verified ? "verified" : "unverified")
        );
    }

    public static void LoginRejected()
    {
        _loginResults.Add(1, new KeyValuePair<string, object?>("result", "rejected"));
    }

    public static void QueueFull()
    {
        _dropped.Add(1, new KeyValuePair<string, object?>("reason", "queue_full"));
    }

    public static void TooLong()
    {
        _dropped.Add(1, new KeyValuePair<string, object?>("reason", "too_long"));
    }
}
