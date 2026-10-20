namespace AprsNet.Packets;

/// <summary>
/// What a GPS says about where it is: from one NMEA sentence (<see cref="NmeaSentence.Fix"/>), from a GPS's sentences
/// put together (<see cref="NmeaFixTracker"/>), or from gpsd. Each value is <see langword="null"/> when the source
/// didn't give it.
/// </summary>
public sealed record GpsFix
{
    /// <summary>Whether the GPS has a fix, and of what kind.</summary>
    public GpsFixMode Mode { get; init; }

    /// <summary>
    /// The position, in decimal degrees. <see langword="null"/> when the GPS has no fix (<see cref="GpsFixMode.NoFix"/>)
    /// even if it sent one, since a GPS without a fix sends its last or a made-up position.
    /// </summary>
    public AprsPosition? Position { get; init; }

    /// <summary>The time of day of the fix, in UTC.</summary>
    public TimeOnly? UtcTime { get; init; }

    /// <summary>The date of the fix, in UTC. Of the NMEA sentences, only RMC carries it.</summary>
    public DateOnly? UtcDate { get; init; }

    /// <summary>When the fix was taken, once both <see cref="UtcDate"/> and <see cref="UtcTime"/> are known.</summary>
    public DateTimeOffset? Time =>
        UtcDate is { } date && UtcTime is { } time ? new DateTimeOffset(date.ToDateTime(time), TimeSpan.Zero) : null;

    /// <summary>Speed over the ground, in knots.</summary>
    public double? SpeedKnots { get; init; }

    /// <summary>Course over the ground, in degrees from true north.</summary>
    public double? Course { get; init; }

    /// <summary>Altitude above mean sea level, in metres.</summary>
    public double? AltitudeMeters { get; init; }

    /// <summary>How many satellites the fix uses.</summary>
    public int? Satellites { get; init; }

    /// <summary>The horizontal dilution of precision: lower is better, and above about 5 the position is poor.</summary>
    public double? Hdop { get; init; }
}

/// <summary>Whether a GPS has a fix, and of what kind.</summary>
public enum GpsFixMode
{
    /// <summary>Not said: the source doesn't report it (a VTG sentence, say).</summary>
    Unknown,

    /// <summary>No fix: the position, if any was sent, is not to be used.</summary>
    NoFix,

    /// <summary>A fix, without saying whether it has altitude (GGA, RMC and GLL say only that it is valid).</summary>
    Fix,

    /// <summary>A two-dimensional fix: a position without a reliable altitude.</summary>
    Fix2D,

    /// <summary>A three-dimensional fix: a position and altitude.</summary>
    Fix3D,
}
