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

    public static void Packet()
    {
        _linesReceived.Add(1, new KeyValuePair<string, object?>("kind", "packet"));
    }

    public static void ServerComment()
    {
        _linesReceived.Add(1, new KeyValuePair<string, object?>("kind", "server_comment"));
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
