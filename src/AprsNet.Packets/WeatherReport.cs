namespace AprsNet.Packets;

/// <summary>
/// A positionless weather report (<c>_</c>, APRS101 ch. 12): a month-day-hour-minute timestamp and weather readings
/// from a station whose position is sent separately.
/// </summary>
public sealed class WeatherReport : AprsPayload
{
    /// <summary>
    /// Creates a positionless weather report to encode with <see cref="AprsPacket.Create"/>. Set
    /// <see cref="Timestamp"/> and <see cref="Weather"/>, and optionally a comment. APRS 1.1 recommends the complete
    /// format instead, a <see cref="PositionReport"/> with the weather station symbol, which also carries snowfall.
    /// </summary>
    public WeatherReport() { }

    /// <summary>
    /// When the readings were taken, in the <see cref="AprsTimestampFormat.MonthDayHourMinute"/> format. Required when
    /// encoding. <see langword="null"/> when the station sent a time that cannot be read, such as the
    /// <c>00000000</c> of a station without a clock; the readings are still decoded, with an
    /// <see cref="ParseIssueCode.InvalidTimestamp"/> warning.
    /// </summary>
    public AprsTimestamp? Timestamp { get; init; }

    /// <summary>The readings.</summary>
    public WeatherData Weather { get; init; } = new();

    /// <summary>The timestamp and readings as sent, so a parsed report is rebuilt with their original spelling.</summary>
    internal ReadOnlyMemory<byte>? ReadingsBytes { get; init; }

    /// <summary>
    /// The text after the readings, exactly as sent. APRS101 puts a software type byte and a 2–4 character unit
    /// type here (<c>wRSW</c>); many stations send free text instead.
    /// </summary>
    public ReadOnlyMemory<byte> CommentBytes { get; internal init; }

    /// <summary>
    /// The text after the readings: decoded as UTF-8 when the bytes are valid UTF-8, otherwise one character per
    /// byte (Latin-1).
    /// </summary>
    public string Comment { get; init; } = "";
}
