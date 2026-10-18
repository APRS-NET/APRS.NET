namespace AprsNet.Packets;

/// <summary>
/// A payload that places something on the map: a <see cref="PositionReport"/>, an <see cref="ObjectReport"/> or an
/// <see cref="ItemReport"/>. All three carry the same position, symbol, data extensions and comment (APRS101 ch. 7–9
/// and 11), so a map can handle them with one <c>case PositionedPayload</c>.
/// </summary>
public abstract class PositionedPayload : AprsPayload
{
    private protected PositionedPayload() { }

    private protected PositionedPayload(PositionFields fields)
    {
        Position = fields.Position;
        Symbol = fields.Symbol;
        Timestamp = fields.Timestamp;
        IsCompressed = fields.IsCompressed;
        Course = fields.Course;
        SpeedKnots = fields.SpeedKnots;
        AltitudeFeet = fields.AltitudeFeet;
        PowerHeightGain = fields.PowerHeightGain;
        RangeMiles = fields.RangeMiles;
        DirectionFindingStrength = fields.DirectionFindingStrength;
        DirectionFinding = fields.DirectionFinding;
        CompressionType = fields.CompressionType;
        Area = fields.Area;
        Signpost = fields.Signpost;
        Weather = fields.Weather;
        Datum = fields.Datum;
        Telemetry = fields.Telemetry;
        CommentBytes = fields.CommentBytes;
        Comment = fields.Comment;
        ExtensionBytes = fields.ExtensionBytes;
        WeatherRestBytes = fields.WeatherRestBytes;
    }

    /// <summary>
    /// The data extension as sent (uncompressed), or the compressed position's <c>csT</c> bytes. The format has several
    /// spellings of one value (unknown course and speed as dots or spaces, ignored <c>csT</c> bytes), so a parsed
    /// payload is rebuilt with the bytes it arrived with. For Mic-E it is the type, longitude, speed and course bytes.
    /// Callers cannot set it.
    /// </summary>
    internal ReadOnlyMemory<byte> ExtensionBytes { get; init; }

    /// <summary>For a weather station, the comment bytes after the readings: where reading stopped.</summary>
    internal ReadOnlyMemory<byte> WeatherRestBytes { get; init; }

    /// <summary>The position. With a <c>!DAO!</c> token the extra precision is already applied.</summary>
    public AprsPosition Position { get; init; }

    /// <summary>The symbol.</summary>
    public AprsSymbol Symbol { get; init; }

    /// <summary>When the position was valid: optional on position reports, required on objects, absent on items.</summary>
    public AprsTimestamp? Timestamp { get; init; }

    /// <summary>Whether the position was sent in the compressed format (APRS101 ch. 9).</summary>
    public bool IsCompressed { get; init; }

    /// <summary>Whether the sender marked the position as unknown: <c>\.</c> at 0° N 0° W (APRS101 ch. 6).</summary>
    public bool IsPositionUnknown => Symbol == new AprsSymbol('\\', '.') && Position is { Latitude: 0, Longitude: 0 };

    /// <summary>
    /// Course over the ground in degrees as sent, 0 to 360, or <see langword="null"/> when not sent. APRS allows
    /// <c>000</c> to mean unknown, so 0 may not be north. For the weather station symbol this is the wind direction.
    /// </summary>
    public int? Course { get; init; }

    /// <summary>
    /// Speed in knots, or <see langword="null"/> when not sent. For the weather station symbol this is the wind speed,
    /// which stations send in mph; <see cref="Weather"/> has it as <see cref="WeatherData.WindSpeedMph"/>.
    /// </summary>
    public double? SpeedKnots { get; init; }

    /// <summary>Altitude in feet, from a compressed position or a <c>/A=</c> comment field.</summary>
    public double? AltitudeFeet { get; init; }

    /// <summary>Power, height, gain and directivity (<c>PHG</c>).</summary>
    public PowerHeightGain? PowerHeightGain { get; init; }

    /// <summary>Radio range in miles (<c>RNG</c>, or the range form of a compressed position).</summary>
    public double? RangeMiles { get; init; }

    /// <summary>Direction-finding signal strength (<c>DFS</c>).</summary>
    public DirectionFindingStrength? DirectionFindingStrength { get; init; }

    /// <summary>A direction-finding bearing report (<c>/BRG/NRQ</c> after course and speed).</summary>
    public DirectionFindingReport? DirectionFinding { get; internal init; }

    /// <summary>The compression type byte of a compressed position that carries course, speed, range or altitude.</summary>
    public CompressionType? CompressionType { get; internal init; }

    /// <summary>The shape an area symbol (<c>\l</c>) draws, from its <c>Tyy/Cxx</c> extension (APRS101 ch. 11).</summary>
    public AreaShape? Area { get; internal init; }

    /// <summary>The 1–3 character text a signpost symbol (<c>\m</c>) shows, from <c>{55}</c> in the comment.</summary>
    public string? Signpost { get; internal init; }

    /// <summary>
    /// Weather readings from a weather station symbol (<c>_</c>, any table): wind from <see cref="Course"/> and
    /// <see cref="SpeedKnots"/> and the other readings from the start of the comment (APRS101 ch. 12).
    /// <see langword="null"/> for other symbols, or when the comment holds no readings. When set on a payload to
    /// encode, the wind is sent as the course and speed and the other readings at the start of the comment.
    /// </summary>
    public WeatherData? Weather { get; init; }

    /// <summary>The datum named by a <c>!DAO!</c> token (APRS 1.2), for example <c>W</c> for WGS84.</summary>
    public char? Datum { get; internal init; }

    /// <summary>
    /// Base-91 comment telemetry (APRS 1.2): <c>|SSAABB…|</c> at the end of the comment, read into a sequence number,
    /// one to five analog values (0 to 8280) and, after all five, the eight digital channels. The station's
    /// <c>PARM.</c>, <c>UNIT.</c> and <c>EQNS.</c> definitions apply as they do to <c>T#</c> reports. Not part of
    /// <see cref="Comment"/>. When set on a payload to encode, it is written after the comment.
    /// </summary>
    public TelemetryReport? Telemetry { get; init; }

    /// <summary>The comment exactly as sent: every byte after the position and data extension.</summary>
    public ReadOnlyMemory<byte> CommentBytes { get; internal init; }

    /// <summary>
    /// The comment as text, without a <c>!DAO!</c> token, <see cref="Telemetry"/> or the <see cref="Weather"/> readings: decoded as UTF-8 when the bytes are valid UTF-8, otherwise
    /// one character per byte (Latin-1). APRS has no text encoding, so this is a best guess.
    /// </summary>
    public string Comment { get; init; } = "";
}
