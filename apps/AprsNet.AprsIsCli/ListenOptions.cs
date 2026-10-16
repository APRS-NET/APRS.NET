using AprsNet.AprsIsTcpClient;

namespace AprsNet.AprsIsCli;

/// <summary>Validated settings for <c>aprscli listen</c>.</summary>
internal sealed record ListenOptions
{
    public required AprsIsEndpoint Endpoint { get; init; }

    /// <summary>Connect over TLS.</summary>
    public bool UseTls { get; init; }

    public required string Callsign { get; init; }

    /// <summary>Never printed or logged.</summary>
    public required int Passcode { get; init; }

    public string? Filter { get; init; }

    public bool Timestamps { get; init; }

    public OutputFormat Format { get; init; }

    /// <summary>Colour the pretty format with ANSI escape codes.</summary>
    public bool Color { get; init; }

    public string? Capture { get; init; }

    public bool Quiet { get; init; }

    /// <summary>Also log each packet to OpenTelemetry (never to the console).</summary>
    public bool LogPackets { get; init; }

    public int? Count { get; init; }

    public TimeSpan? Duration { get; init; }

    /// <summary>Reconnect with backoff when the connection drops or goes silent.</summary>
    public bool Reconnect { get; init; }

    /// <summary>Treat the connection as failed after this long without a packet, even if keepalives continue. Null disables.</summary>
    public TimeSpan? MaxSilence { get; init; }

    /// <summary>Print a summary to standard error this often, and once at the end. Null turns statistics off.</summary>
    public TimeSpan? StatsInterval { get; init; }

    public override string ToString() => $"{Callsign}@{Endpoint}"; // no passcode, even by accident
}
