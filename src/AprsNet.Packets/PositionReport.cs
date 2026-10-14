namespace AprsNet.Packets;

/// <summary>
/// A position report (<c>!</c>, <c>=</c>, <c>/</c> or <c>@</c>, APRS101 ch. 8 and 9): where a station is, its symbol,
/// and the optional data extensions and comment that follow.
/// </summary>
public sealed class PositionReport : PositionedPayload
{
    /// <summary>
    /// Creates a position report to encode with <see cref="AprsPacket.Create"/>. Set <see cref="PositionedPayload.Position"/>
    /// and <see cref="PositionedPayload.Symbol"/>, and optionally a timestamp, one data extension (course and speed,
    /// PHG, range or DF signal strength), an altitude and a comment.
    /// </summary>
    public PositionReport() { }

    internal PositionReport(PositionFields fields)
        : base(fields) { }

    /// <summary>Whether the station can receive APRS messages (<c>=</c> and <c>@</c>).</summary>
    public bool IsMessagingCapable { get; init; }
}
