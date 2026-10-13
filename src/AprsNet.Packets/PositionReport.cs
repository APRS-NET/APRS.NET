namespace AprsNet.Packets;

/// <summary>
/// A position report (<c>!</c>, <c>=</c>, <c>/</c> or <c>@</c>, APRS101 ch. 8 and 9): where a station is, its symbol,
/// and the optional data extensions and comment that follow.
/// </summary>
public sealed class PositionReport : AprsPayload
{
    internal PositionReport() { }

    /// <summary>The position. With a <c>!DAO!</c> token the extra precision is already applied.</summary>
    public AprsPosition Position { get; internal init; }

    /// <summary>The station's symbol.</summary>
    public AprsSymbol Symbol { get; internal init; }

    /// <summary>When the position was valid, for <c>/</c> and <c>@</c> reports.</summary>
    public AprsTimestamp? Timestamp { get; internal init; }

    /// <summary>Whether the station can receive APRS messages (<c>=</c> and <c>@</c>).</summary>
    public bool IsMessagingCapable { get; internal init; }

    /// <summary>Whether the position was sent in the compressed format (APRS101 ch. 9).</summary>
    public bool IsCompressed { get; internal init; }

    /// <summary>Whether the sender marked its position as unknown: <c>\.</c> at 0° N 0° W (APRS101 ch. 6).</summary>
    public bool IsPositionUnknown => Symbol == new AprsSymbol('\\', '.') && Position is { Latitude: 0, Longitude: 0 };

    /// <summary>
    /// Course over the ground in degrees as sent, 0 to 360, or <see langword="null"/> when not sent. APRS allows
    /// <c>000</c> to mean unknown, so 0 may not be north. For the weather station symbol this is the wind direction.
    /// </summary>
    public int? Course { get; internal init; }

    /// <summary>Speed in knots, or <see langword="null"/> when not sent. For the weather station symbol this is wind speed.</summary>
    public double? SpeedKnots { get; internal init; }

    /// <summary>Altitude in feet, from a compressed position or a <c>/A=</c> comment field.</summary>
    public double? AltitudeFeet { get; internal init; }

    /// <summary>Power, height, gain and directivity (<c>PHG</c>).</summary>
    public PowerHeightGain? PowerHeightGain { get; internal init; }

    /// <summary>Radio range in miles (<c>RNG</c>, or the range form of a compressed position).</summary>
    public double? RangeMiles { get; internal init; }

    /// <summary>Direction-finding signal strength (<c>DFS</c>).</summary>
    public DirectionFindingStrength? DirectionFindingStrength { get; internal init; }

    /// <summary>A direction-finding bearing report (<c>/BRG/NRQ</c> after course and speed).</summary>
    public DirectionFindingReport? DirectionFinding { get; internal init; }

    /// <summary>The compression type byte of a compressed position that carries course, speed, range or altitude.</summary>
    public CompressionType? CompressionType { get; internal init; }

    /// <summary>The datum named by a <c>!DAO!</c> token (APRS 1.2), for example <c>W</c> for WGS84.</summary>
    public char? Datum { get; internal init; }

    /// <summary>The comment exactly as sent: every byte after the position and data extension.</summary>
    public ReadOnlyMemory<byte> CommentBytes { get; internal init; }

    /// <summary>
    /// The comment as text, without a <c>!DAO!</c> token: decoded as UTF-8 when the bytes are valid UTF-8, otherwise
    /// one character per byte (Latin-1). APRS has no text encoding, so this is a best guess.
    /// </summary>
    public string Comment { get; internal init; } = "";
}
