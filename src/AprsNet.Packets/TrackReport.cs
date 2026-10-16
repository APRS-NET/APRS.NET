namespace AprsNet.Packets;

/// <summary>
/// One position report in a station's track, as <see cref="TrackAnalysis"/> sees it: where the station said it was,
/// when the report arrived, and when the station said it was there.
/// </summary>
/// <param name="Position">The reported position, with its ambiguity.</param>
/// <param name="ReceivedAt">When the report arrived.</param>
/// <param name="ReportedAt">
/// The time the report carries, resolved to UTC, or <see langword="null"/> when it carries none or only local time.
/// </param>
/// <param name="HasWeather">
/// Whether the report carries weather readings. A weather station stays put while its readings change, so its
/// reports are never a <see cref="TrackAnomalies.RepeatedPosition"/>.
/// </param>
public readonly record struct TrackReport(
    AprsPosition Position,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ReportedAt = null,
    bool HasWeather = false
)
{
    /// <summary>
    /// The track report in <paramref name="packet"/>, received at <paramref name="receivedAt"/>, or
    /// <see langword="null"/> when the packet has no position or marks it as unknown.
    /// </summary>
    /// <remarks>
    /// Position reports, Mic-E reports, objects and items all count. An object or item is a track of its own, kept
    /// under its name rather than the callsign that sent it; that choice is the caller's.
    /// </remarks>
    public static TrackReport? FromPacket(AprsPacket packet, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return packet.Payload is PositionedPayload { IsPositionUnknown: false } positioned
            ? new TrackReport(
                positioned.Position,
                receivedAt,
                positioned.Timestamp?.ToDateTimeOffset(receivedAt),
                positioned.Weather is not null
            )
            : null;
    }
}
