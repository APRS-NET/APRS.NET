namespace AprsNet.Packets;

/// <summary>
/// The fields of a <see cref="PositionedPayload"/> while a decoder fills them in. The payload's properties are
/// init-only, so decoders collect the values here and the payload copies them in its constructor.
/// </summary>
internal sealed class PositionFields
{
    public AprsPosition Position { get; set; }

    public AprsSymbol Symbol { get; set; }

    public AprsTimestamp? Timestamp { get; set; }

    public bool IsCompressed { get; set; }

    public int? Course { get; set; }

    public double? SpeedKnots { get; set; }

    public double? AltitudeFeet { get; set; }

    public PowerHeightGain? PowerHeightGain { get; set; }

    public double? RangeMiles { get; set; }

    public DirectionFindingStrength? DirectionFindingStrength { get; set; }

    public DirectionFindingReport? DirectionFinding { get; set; }

    public CompressionType? CompressionType { get; set; }

    public AreaShape? Area { get; set; }

    public string? Signpost { get; set; }

    public WeatherData? Weather { get; set; }

    public char? Datum { get; set; }

    public ReadOnlyMemory<byte> CommentBytes { get; set; }

    public string Comment { get; set; } = "";

    public ReadOnlyMemory<byte> ExtensionBytes { get; set; }
}
