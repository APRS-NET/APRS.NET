using AprsNet.Packets;

namespace AprsNet.Beacons;

/// <summary>
/// Where a station probably is now, worked out from a beacon's position, course and speed: what other stations assume
/// when they show it moving on from its last report. A station compares it with where it really is to see how far its
/// last beacon has drifted, and beacons again once that is too far (<see cref="BeaconSchedule.Drifted"/>).
/// </summary>
/// <remarks>
/// The station is taken to keep its course and speed along a great circle (<see cref="Geodesy.Destination"/>). It does
/// not turn or stop, so the estimate grows worse with time; how long to trust it is the app's choice.
/// </remarks>
/// <param name="Position">Where the station was.</param>
/// <param name="Course">Its course over the ground, in degrees from true north (0 and 360 are both north).</param>
/// <param name="SpeedKnots">Its speed in knots; 0 for a station that isn't moving.</param>
/// <param name="At">When it was there: when the beacon was sent, or heard.</param>
/// <example>
/// <code>
/// var sent = new DeadReckoning(here, 90, 48.6, now);            // 90 km/h east
/// var assumed = sent.PositionAt(now + TimeSpan.FromMinutes(2)); // about 3 km east of here
/// </code>
/// </example>
public readonly record struct DeadReckoning(AprsPosition Position, double Course, double SpeedKnots, DateTimeOffset At)
{
    private const double KilometresPerNauticalMile = 1.852;

    /// <summary>The course over the ground, in degrees from true north (0 and 360 are both north).</summary>
    /// <exception cref="ArgumentOutOfRangeException">On creation, when the course is not 0 to 360.</exception>
    public double Course { get; } =
        Course is >= 0 and <= 360
            ? Course
            : throw new ArgumentOutOfRangeException(nameof(Course), Course, "The course must be 0 to 360 degrees.");

    /// <summary>The speed in knots.</summary>
    /// <exception cref="ArgumentOutOfRangeException">On creation, when the speed is negative or not finite.</exception>
    public double SpeedKnots { get; } =
        SpeedKnots >= 0 && double.IsFinite(SpeedKnots)
            ? SpeedKnots
            : throw new ArgumentOutOfRangeException(nameof(SpeedKnots), SpeedKnots, "The speed must be 0 or more.");

    /// <summary>
    /// The dead reckoning for a position report, object or item that carries a course and speed, as of
    /// <paramref name="at"/>, when it was sent or heard.
    /// </summary>
    /// <param name="report">The report.</param>
    /// <param name="at">When the station was at the reported position.</param>
    /// <param name="deadReckoning">The dead reckoning, when the report has one.</param>
    /// <returns>
    /// <see langword="false"/> when the report has no course or speed, has a course of 0 (which APRS allows to mean
    /// unknown), or marks its position as unknown.
    /// </returns>
    public static bool TryFromReport(PositionedPayload report, DateTimeOffset at, out DeadReckoning deadReckoning)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (
            report is
            { Course: > 0 and <= 360 and var course, SpeedKnots: >= 0 and var speed, IsPositionUnknown: false }
        )
        {
            deadReckoning = new DeadReckoning(report.Position, course, speed, at);
            return true;
        }

        deadReckoning = default;
        return false;
    }

    /// <summary>How far the station has gone since <see cref="At"/>, in kilometres; 0 at or before it.</summary>
    /// <param name="time">The time to work it out for.</param>
    public double DistanceKmAt(DateTimeOffset time) =>
        time > At ? SpeedKnots * KilometresPerNauticalMile * (time - At).TotalHours : 0;

    /// <summary>
    /// Where the station is assumed to be at <paramref name="time"/>: <see cref="Position"/> at or before
    /// <see cref="At"/>, or for a station that isn't moving.
    /// </summary>
    /// <param name="time">The time to work it out for.</param>
    public AprsPosition PositionAt(DateTimeOffset time) =>
        DistanceKmAt(time) is > 0 and var distance ? Geodesy.Destination(Position, Course, distance) : Position;

    /// <summary>
    /// How far <paramref name="actual"/>, where the station really is at <paramref name="time"/>, is from where it is
    /// assumed to be.
    /// </summary>
    /// <param name="actual">Where the station really is, from its GPS.</param>
    /// <param name="time">When it is there.</param>
    public DeadReckoningError ErrorAt(AprsPosition actual, DateTimeOffset time)
    {
        var assumed = PositionAt(time);
        return new DeadReckoningError(
            Geodesy.DistanceKm(assumed, actual),
            Geodesy.InitialBearing(assumed, actual),
            assumed
        );
    }
}

/// <summary>How far a station really is from where its last beacon makes others assume it is.</summary>
/// <param name="DistanceKm">The distance between the two, in kilometres.</param>
/// <param name="Bearing">
/// The bearing from the assumed position to the real one, in degrees from true north, 0 to under 360; 0 when they are the
/// same.
/// </param>
/// <param name="Assumed">Where others assume the station is.</param>
public readonly record struct DeadReckoningError(double DistanceKm, double Bearing, AprsPosition Assumed);
