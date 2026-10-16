namespace AprsNet.Packets;

/// <summary>What looks wrong about a station's new position report, given its track so far.</summary>
[Flags]
public enum TrackAnomalies
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>
    /// The same position was reported again after the duplicate window: either the station's latest position, sooner
    /// than <see cref="TrackAnalysisOptions.RepeatedPositionInterval"/> (a tracker without a fix, or a fixed station
    /// beaconing too often), or an earlier position it had moved away from, within
    /// <see cref="TrackAnalysisOptions.LateCopyInterval"/> (a late copy of an earlier report). Reports that carry weather
    /// readings are exempt.
    /// </summary>
    RepeatedPosition = 1,

    /// <summary>
    /// Getting here from the previous position would take more than <see cref="TrackAnalysisOptions.MaxSpeedKmh"/>.
    /// </summary>
    ImpossibleSpeed = 2,

    /// <summary>
    /// The report arrived less than <see cref="TrackAnalysisOptions.RapidRepeatInterval"/> after the previous one.
    /// </summary>
    RapidRepeat = 4,

    /// <summary>The report's own time is earlier than that of the latest timestamped report before it.</summary>
    TimeBackwards = 8,

    /// <summary>
    /// The position moved because its ambiguity changed, not because the station did: the two ambiguity areas overlap.
    /// </summary>
    AmbiguityJump = 16,
}

/// <summary>Limits for <see cref="TrackAnalysis"/>. Every limit can be changed; the defaults suit most stations.</summary>
public sealed record TrackAnalysisOptions
{
    /// <summary>The default limits.</summary>
    public static TrackAnalysisOptions Default { get; } = new();

    /// <summary>
    /// The fastest believable speed, in km/h. The default, 3,000 km/h, is far above any aircraft or balloon; a map
    /// can pass a lower limit for stations whose symbol isn't an aircraft or balloon.
    /// </summary>
    public double MaxSpeedKmh { get; init; } = 3000;

    /// <summary>
    /// How long after a report the same position counts as a duplicate rather than a repeat: 30 seconds by default,
    /// the APRS-IS duplicate window.
    /// </summary>
    public TimeSpan DuplicateWindow { get; init; } = DuplicateFilter.DefaultWindow;

    /// <summary>
    /// How soon the same position may be resent before it is flagged as <see cref="TrackAnomalies.RepeatedPosition"/>:
    /// 2 minutes by default. Many fixed stations beacon every few minutes, so a longer interval flags much of the
    /// network: over a quarter of real reports at 10 minutes, about a ninth at 2.
    /// </summary>
    public TimeSpan RepeatedPositionInterval { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long after a report an exact copy of its position, arriving after the station had moved on, is taken for
    /// a late copy (<see cref="TrackAnomalies.RepeatedPosition"/>) rather than a move back: 30 minutes by default.
    /// Digipeated copies arrive minutes late, and a moving station rarely returns to exactly the same coordinates.
    /// </summary>
    public TimeSpan LateCopyInterval { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Reports closer together than this are flagged as <see cref="TrackAnomalies.RapidRepeat"/>: 1 second by default.
    /// </summary>
    public TimeSpan RapidRepeatInterval { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>What <see cref="TrackAnalysis.Check"/> found for a new report, with the figures behind each flag.</summary>
/// <param name="Anomalies">What looks wrong, or <see cref="TrackAnomalies.None"/>.</param>
/// <param name="Previous">The latest report in the track before this one, or <see langword="null"/> for the first.</param>
/// <param name="Interval">How long after <paramref name="Previous"/> this report arrived.</param>
/// <param name="DistanceKm">
/// The distance from <paramref name="Previous"/> in km: the least the station can have moved, given the ambiguity of
/// both positions. <see langword="null"/> when this report repeats an earlier one, which is not a move.
/// </param>
/// <param name="SpeedKmh">
/// The speed that distance implies, in km/h, or <see langword="null"/> when <paramref name="DistanceKm"/> is.
/// Infinity when the station moved with no time between the reports.
/// </param>
/// <param name="RepeatOf">The earlier report whose position this one repeats, for <see cref="TrackAnomalies.RepeatedPosition"/>.</param>
public readonly record struct TrackCheck(
    TrackAnomalies Anomalies,
    TrackReport? Previous,
    TimeSpan? Interval,
    double? DistanceKm,
    double? SpeedKmh,
    TrackReport? RepeatOf
);

/// <summary>
/// Checks a station's new position report against its track so far, for reports that look wrong: positions repeated
/// too often, impossible jumps, reports too close together, time running backwards, and moves that only reflect a
/// change of ambiguity. It flags reports and leaves what to show or keep to the caller.
/// </summary>
/// <remarks>
/// Pass reports that passed a <see cref="DuplicateFilter"/>, so that copies of one packet heard by several IGates are
/// not counted as reports of their own. The analysis keeps no state: the caller keeps each station's track.
/// </remarks>
public static class TrackAnalysis
{
    /// <summary>Checks <paramref name="report"/> against <paramref name="track"/>.</summary>
    /// <param name="track">The station's earlier reports, oldest first. Only the most recent are needed: those within
    /// <see cref="TrackAnalysisOptions.LateCopyInterval"/>, and the latest timestamped one.</param>
    /// <param name="report">The new report.</param>
    /// <param name="options">Limits, or <see langword="null"/> for <see cref="TrackAnalysisOptions.Default"/>.</param>
    public static TrackCheck Check(
        IReadOnlyList<TrackReport> track,
        TrackReport report,
        TrackAnalysisOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(track);
        options ??= TrackAnalysisOptions.Default;
        if (track.Count == 0)
        {
            return default;
        }

        var previous = track[^1];
        var interval = report.ReceivedAt - previous.ReceivedAt;
        var anomalies = TrackAnomalies.None;

        // A copy changed in transit, such as a digipeater adding a space, keeps the position and the time it carries.
        var isAlteredCopy =
            report.ReportedAt is { } reported
            && reported == previous.ReportedAt
            && report.Position == previous.Position;
        if (interval >= TimeSpan.Zero && interval < options.RapidRepeatInterval && !isAlteredCopy)
        {
            anomalies |= TrackAnomalies.RapidRepeat;
        }

        if (report.ReportedAt is { } reportedAt && LatestReportedAt(track) is { } latest && reportedAt < latest)
        {
            anomalies |= TrackAnomalies.TimeBackwards;
        }

        var repeatIndex = report.HasWeather ? -1 : FindRepeat(track, report, options);
        if (repeatIndex >= 0)
        {
            anomalies |= TrackAnomalies.RepeatedPosition;
            if (repeatIndex < track.Count - 1)
            {
                // An earlier position coming back is a late copy, not a move: measuring it from the latest report
                // would make it look like a jump.
                return new TrackCheck(anomalies, previous, interval, null, null, track[repeatIndex]);
            }
        }

        var before = previous.Position.GetAmbiguityBounds();
        var after = report.Position.GetAmbiguityBounds();
        if (
            report.Position.Ambiguity != previous.Position.Ambiguity
            && report.Position != previous.Position with { Ambiguity = report.Position.Ambiguity }
            && Overlap(before, after)
        )
        {
            anomalies |= TrackAnomalies.AmbiguityJump;
        }

        var distanceKm = Math.Max(
            0,
            Geodesy.DistanceKm(before.Center, after.Center) - HalfDiagonalKm(before) - HalfDiagonalKm(after)
        );
        var elapsed = Elapsed(previous, report);
        var speedKmh =
            distanceKm == 0 ? 0
            : elapsed > TimeSpan.Zero ? distanceKm / elapsed.TotalHours
            : double.PositiveInfinity;
        if (speedKmh > options.MaxSpeedKmh)
        {
            anomalies |= TrackAnomalies.ImpossibleSpeed;
        }

        return new TrackCheck(
            anomalies,
            previous,
            interval,
            distanceKm,
            speedKmh,
            repeatIndex >= 0 ? track[repeatIndex] : null
        );
    }

    // The report this one repeats, after the duplicate window: the latest, if the station hasn't moved and resent it
    // within the repeat interval, or an earlier one it had moved away from, within the late-copy interval.
    private static int FindRepeat(IReadOnlyList<TrackReport> track, TrackReport report, TrackAnalysisOptions options)
    {
        var latest = track.Count - 1;
        if (track[latest].Position == report.Position)
        {
            var since = report.ReceivedAt - track[latest].ReceivedAt;
            return since >= options.DuplicateWindow && since < options.RepeatedPositionInterval ? latest : -1;
        }

        for (var i = latest - 1; i >= 0; i--)
        {
            var since = report.ReceivedAt - track[i].ReceivedAt;
            if (since >= options.LateCopyInterval)
            {
                break;
            }

            if (since >= options.DuplicateWindow && track[i].Position == report.Position)
            {
                return i;
            }
        }

        return -1;
    }

    private static DateTimeOffset? LatestReportedAt(IReadOnlyList<TrackReport> track)
    {
        for (var i = track.Count - 1; i >= 0; i--)
        {
            if (track[i].ReportedAt is { } reportedAt)
            {
                return reportedAt;
            }
        }

        return null;
    }

    // The longer of the gaps between arrivals and between reported times, so that a report delayed on its way, or
    // one whose time is only to the minute, is not taken for a fast move.
    private static TimeSpan Elapsed(TrackReport previous, TrackReport report)
    {
        var received = report.ReceivedAt - previous.ReceivedAt;
        return report.ReportedAt is { } to && previous.ReportedAt is { } from && to - from > received
            ? to - from
            : received;
    }

    private static bool Overlap(AprsBounds a, AprsBounds b) =>
        a.SouthWest.Latitude <= b.NorthEast.Latitude
        && b.SouthWest.Latitude <= a.NorthEast.Latitude
        && a.SouthWest.Longitude <= b.NorthEast.Longitude
        && b.SouthWest.Longitude <= a.NorthEast.Longitude;

    private static double HalfDiagonalKm(AprsBounds bounds) => Geodesy.DistanceKm(bounds.Center, bounds.NorthEast);
}
