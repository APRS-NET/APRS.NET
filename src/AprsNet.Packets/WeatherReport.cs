namespace AprsNet.Packets;

/// <summary>
/// A positionless weather report (<c>_</c>, APRS101 ch. 12): a month-day-hour-minute timestamp and weather readings
/// from a station whose position is sent separately.
/// </summary>
public sealed class WeatherReport : AprsPayload
{
    internal WeatherReport() { }

    /// <summary>When the readings were taken, in the <see cref="AprsTimestampFormat.MonthDayHourMinute"/> format.</summary>
    public AprsTimestamp Timestamp { get; internal init; }

    /// <summary>The readings.</summary>
    public WeatherData Weather { get; internal init; } = new();

    /// <summary>
    /// The text after the readings, exactly as sent. APRS101 puts a software type byte and a 2–4 character unit
    /// type here (<c>wRSW</c>); many stations send free text instead.
    /// </summary>
    public ReadOnlyMemory<byte> CommentBytes { get; internal init; }

    /// <summary>
    /// The text after the readings: decoded as UTF-8 when the bytes are valid UTF-8, otherwise one character per
    /// byte (Latin-1).
    /// </summary>
    public string Comment { get; internal init; } = "";
}
