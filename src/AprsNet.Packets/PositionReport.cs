namespace AprsNet.Packets;

/// <summary>
/// A position report (<c>!</c>, <c>=</c>, <c>/</c> or <c>@</c>, APRS101 ch. 8 and 9): where a station is, its symbol,
/// and the optional data extensions and comment that follow.
/// </summary>
public sealed class PositionReport : PositionedPayload
{
    internal PositionReport() { }

    /// <summary>Whether the station can receive APRS messages (<c>=</c> and <c>@</c>).</summary>
    public bool IsMessagingCapable { get; internal init; }
}
