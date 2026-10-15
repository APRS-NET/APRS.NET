namespace AprsNet.Packets;

/// <summary>
/// A telemetry report (<c>T#</c>, APRS101 ch. 13): a sequence number, up to five analog channels and eight digital
/// channels, then an optional comment. The station's <see cref="TelemetryDefinition"/> messages name, label and
/// scale the channels.
/// </summary>
public sealed class TelemetryReport : AprsPayload
{
    /// <summary>
    /// Creates a telemetry report to encode with <see cref="AprsPacket.Create"/>. Set <see cref="SequenceNumber"/>
    /// and one to five <see cref="Analog"/> values; add the eight <see cref="Digital"/> channels after all five
    /// analog ones, and a comment after the digital channels.
    /// </summary>
    public TelemetryReport() { }

    /// <summary>
    /// The sequence as sent: usually three digits such as <c>005</c>, or <c>MIC</c>. To encode, set
    /// <see cref="SequenceNumber"/> instead, or this to <c>MIC</c>.
    /// </summary>
    public string Sequence { get; init; } = "";

    /// <summary>The sequence as a number, 0 to 999 when encoding; <see langword="null"/> for <c>MIC</c>.</summary>
    public int? SequenceNumber { get; init; }

    /// <summary>
    /// The raw analog values in channel order, one to five of them. APRS101 sends whole numbers 0–255; many stations
    /// send decimals, negatives or larger values, which are kept as sent. An empty field is <see langword="null"/>.
    /// Apply the station's <see cref="TelemetryEquation"/>s to get readings. To encode, give whole numbers 0 to 999
    /// (APRS 1.2; APRS101 says 0 to 255) and scale them to readings with an <c>EQNS.</c> definition.
    /// </summary>
    public IReadOnlyList<double?> Analog { get; init; } = [];

    /// <summary>The eight digital channels in order, <see langword="true"/> for <c>1</c>; empty when not sent.</summary>
    public IReadOnlyList<bool> Digital { get; init; } = [];

    /// <summary>The sequence and channels as sent, so a parsed report is rebuilt with their original spelling.</summary>
    internal ReadOnlyMemory<byte>? ValuesBytes { get; init; }

    /// <summary>The comment after the digital channels, exactly as sent.</summary>
    public ReadOnlyMemory<byte> CommentBytes { get; internal init; }

    /// <summary>
    /// The comment: decoded as UTF-8 when the bytes are valid UTF-8, otherwise one character per byte (Latin-1).
    /// </summary>
    public string Comment { get; init; } = "";
}
