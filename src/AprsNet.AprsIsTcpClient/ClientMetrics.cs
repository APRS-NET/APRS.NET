using System.Diagnostics.Metrics;

namespace AprsNet.AprsIsTcpClient;

/// <summary>Instruments for the client; see the metrics plan. Tag values are fixed strings to keep cardinality bounded.</summary>
internal static class ClientMetrics
{
    public const string MeterName = "AprsNet.AprsIsTcpClient";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> LinesReceived = Meter.CreateCounter<long>(
        "aprsnet.aprsis.lines.received",
        "{line}",
        "Lines read from the server."
    );

    private static readonly Counter<long> ConnectionAttempts = Meter.CreateCounter<long>(
        "aprsnet.aprsis.connection.attempts",
        "{attempt}",
        "Connection and login attempts."
    );

    private static readonly Counter<long> LoginResults = Meter.CreateCounter<long>(
        "aprsnet.aprsis.login.results",
        "{login}",
        "Server login responses."
    );

    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>(
        "aprsnet.aprsis.packets.dropped",
        "{packet}",
        "Lines discarded before delivery."
    );

    public static void Packet() => LinesReceived.Add(1, new KeyValuePair<string, object?>("kind", "packet"));

    public static void ServerComment() =>
        LinesReceived.Add(1, new KeyValuePair<string, object?>("kind", "server_comment"));

    public static void Attempt(string outcome) =>
        ConnectionAttempts.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void Login(LoginStatus status) =>
        LoginResults.Add(
            1,
            new KeyValuePair<string, object?>("result", status == LoginStatus.Verified ? "verified" : "unverified")
        );

    public static void LoginRejected() => LoginResults.Add(1, new KeyValuePair<string, object?>("result", "rejected"));

    public static void QueueFull() => Dropped.Add(1, new KeyValuePair<string, object?>("reason", "queue_full"));

    public static void TooLong() => Dropped.Add(1, new KeyValuePair<string, object?>("reason", "too_long"));
}
