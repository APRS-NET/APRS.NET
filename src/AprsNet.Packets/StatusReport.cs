namespace AprsNet.Packets;

/// <summary>
/// A status report (<c>&gt;</c>, APRS101 ch. 16): a line of text announcing the station's mission or state, with
/// an optional timestamp, or a Maidenhead locator and symbol, and an optional meteor-scatter beam heading and power.
/// </summary>
public sealed class StatusReport : AprsPayload
{
    /// <summary>
    /// Creates a status report to encode with <see cref="AprsPacket.Create"/>. Set <see cref="Text"/>, and either a
    /// zulu <see cref="AprsTimestampFormat.DayHourMinute"/> <see cref="Timestamp"/>, or a
    /// <see cref="MaidenheadLocator"/> with its <see cref="Symbol"/>.
    /// </summary>
    public StatusReport() { }

    /// <summary>When the status was sent; status reports only use the zulu day-hour-minute form.</summary>
    public AprsTimestamp? Timestamp { get; init; }

    /// <summary>
    /// A 4- or 6-character Maidenhead locator that opens the report (<c>IO91SX</c>), in upper case whatever case it
    /// arrived in. A report with a locator has no timestamp.
    /// </summary>
    public string? MaidenheadLocator { get; init; }

    /// <summary>The symbol that follows a Maidenhead locator.</summary>
    public AprsSymbol? Symbol { get; init; }

    /// <summary>Beam heading in degrees from a trailing <c>^HP</c> code, used in meteor-scatter work.</summary>
    public int? BeamHeading { get; internal init; }

    /// <summary>Effective radiated power in watts from a trailing <c>^HP</c> code.</summary>
    public int? EffectiveRadiatedPowerWatts { get; internal init; }

    /// <summary>
    /// The status text exactly as sent: everything after the timestamp, or after the locator and symbol (where it
    /// starts with the required space). A trailing <c>^HP</c> code stays in the text.
    /// </summary>
    public ReadOnlyMemory<byte> TextBytes { get; internal init; }

    /// <summary>
    /// The status text: decoded as UTF-8 when the bytes are valid UTF-8, otherwise one character per byte (Latin-1).
    /// </summary>
    public string Text { get; init; } = "";
}
