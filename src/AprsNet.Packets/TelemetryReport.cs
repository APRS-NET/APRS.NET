namespace AprsNet.Packets;

/// <summary>
/// A telemetry report (<c>T#</c>, APRS101 ch. 13): a sequence number, up to five analog channels and eight digital
/// channels, then an optional comment. The station's <see cref="TelemetryDefinition"/> messages name, label and
/// scale the channels.
/// </summary>
public sealed class TelemetryReport : AprsPayload
{
    internal TelemetryReport() { }

    /// <summary>The sequence as sent: usually three digits such as <c>005</c>, or <c>MIC</c>.</summary>
    public string Sequence { get; internal init; } = "";

    /// <summary>The sequence as a number; <see langword="null"/> for <c>MIC</c>.</summary>
    public int? SequenceNumber { get; internal init; }

    /// <summary>
    /// The raw analog values in channel order, one to five of them. APRS101 sends whole numbers 0–255; many stations
    /// send decimals, negatives or larger values, which are kept as sent. An empty field is <see langword="null"/>.
    /// Apply the station's <see cref="TelemetryEquation"/>s to get readings.
    /// </summary>
    public IReadOnlyList<double?> Analog { get; internal init; } = [];

    /// <summary>The eight digital channels in order, <see langword="true"/> for <c>1</c>; empty when not sent.</summary>
    public IReadOnlyList<bool> Digital { get; internal init; } = [];

    /// <summary>The comment after the digital channels, exactly as sent.</summary>
    public ReadOnlyMemory<byte> CommentBytes { get; internal init; }

    /// <summary>
    /// The comment: decoded as UTF-8 when the bytes are valid UTF-8, otherwise one character per byte (Latin-1).
    /// </summary>
    public string Comment { get; internal init; } = "";
}
