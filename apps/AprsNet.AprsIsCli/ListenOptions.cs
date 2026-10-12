using AprsNet.AprsIsTcpClient;

namespace AprsNet.AprsIsCli;

/// <summary>Validated settings for <c>aprscli listen</c>.</summary>
internal sealed record ListenOptions
{
    public required AprsIsEndpoint Endpoint { get; init; }

    public required string Callsign { get; init; }

    /// <summary>Never printed or logged.</summary>
    public required int Passcode { get; init; }

    public string? Filter { get; init; }

    public bool Timestamps { get; init; }

    public string? Capture { get; init; }

    public bool Quiet { get; init; }

    /// <summary>Also log each packet to OpenTelemetry (never to the console).</summary>
    public bool LogPackets { get; init; }

    public int? Count { get; init; }

    public TimeSpan? Duration { get; init; }

    public override string ToString() => $"{Callsign}@{Endpoint}"; // no passcode, even by accident
}
