using AprsNet.Packets;

namespace AprsNet.Messaging;

/// <summary>
/// What a station knows about itself and what it has heard, for a <see cref="QueryResponder"/> to answer queries from.
/// Leave out what the station doesn't have: a query about it goes unanswered, as APRS101 allows.
/// </summary>
public sealed class QueryState
{
    /// <summary>
    /// The station's position as it beacons it: a <see cref="PositionReport"/> or <see cref="MicEReport"/>. It answers
    /// <c>?APRSP</c> and <c>?APRS?</c>, and places the station for a query with a footprint.
    /// </summary>
    public PositionedPayload? Position { get; init; }

    /// <summary>The station's status. It answers <c>?APRSS</c> and, with the position, <c>?APRS?</c>.</summary>
    public StatusReport? Status { get; init; }

    /// <summary>
    /// The station's weather, for <c>?WX?</c>: a <see cref="WeatherReport"/>, which is sent with
    /// <see cref="Position"/>, or a position report carrying the weather, sent alone.
    /// </summary>
    public AprsPayload? Weather { get; init; }

    /// <summary>
    /// The station's capabilities, for <c>?IGATE?</c>; an IGate gives
    /// <see cref="CapabilitiesReport.ForIGate"/> with its counts. Stations that aren't IGates leave it out.
    /// </summary>
    public CapabilitiesReport? Capabilities { get; init; }

    /// <summary>The station's objects and items, for <c>?APRSO</c>.</summary>
    public IReadOnlyList<PositionedPayload> Objects { get; init; } = [];

    /// <summary>The stations heard direct, without a digipeater, for <c>?APRSD</c>; most recent first, say.</summary>
    public IReadOnlyList<Callsign> HeardDirect { get; init; } = [];

    /// <summary>
    /// Looks up a station for <c>?APRSH</c>: how often it was heard, and where it was. Returns
    /// <see langword="null"/> for a station not heard.
    /// </summary>
    public Func<Callsign, HeardStation?>? Heard { get; init; }

    /// <summary>
    /// The messages for a station that it hasn't acknowledged or that weren't delivered, for <c>?APRSM</c>; they are
    /// sent again as they are, with their message IDs.
    /// </summary>
    public Func<Callsign, IReadOnlyList<MessagePayload>>? MessagesWaiting { get; init; }
}

/// <summary>A station the queried station has heard, for an <c>?APRSH</c> answer.</summary>
public sealed class HeardStation
{
    /// <summary>
    /// Packets heard from the station in each of the last 8 hours, the current hour first. Fewer than 8 are filled with
    /// zeros.
    /// </summary>
    public IReadOnlyList<int> PacketsPerHour { get; init; } = [];

    /// <summary>
    /// Where the station was last; the answer includes it as an object named after the station. <see langword="null"/>
    /// leaves the object out.
    /// </summary>
    public AprsPosition? Position { get; init; }

    /// <summary>The station's symbol, for the object.</summary>
    public AprsSymbol Symbol { get; init; } = new('/', '/');

    /// <summary>When <see cref="Position"/> was heard; the object's timestamp. Now when not given.</summary>
    public DateTimeOffset? LastHeard { get; init; }
}
