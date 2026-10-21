namespace AprsNet.Packets;

/// <summary>
/// How good a GPS's fix is: the satellites it uses, the dilutions of precision, and its own estimate of its error. From
/// NMEA (<see cref="NmeaFixTracker.Quality"/>: GGA, GSA, GSV and GST) or from gpsd (SKY and TPV reports). Each value is
/// <see langword="null"/> when the source didn't give it.
/// </summary>
/// <remarks>
/// The error estimates are what the receiver or gpsd reports, and they don't all mean the same: gpsd gives them at 95%
/// confidence, while NMEA's GST gives one standard deviation (about 68%), so a GST error looks about half the size.
/// Compare them with each other only from one source.
/// </remarks>
public sealed record GpsQuality
{
    /// <summary>How many satellites the fix uses.</summary>
    public int? SatellitesUsed { get; init; }

    /// <summary>How many satellites the GPS can see, used or not.</summary>
    public int? SatellitesInView { get; init; }

    /// <summary>
    /// The position (3D) dilution of precision. The dilutions say how well the satellites in use are spread across the
    /// sky: lower is better; below 2 is good, and above about 5 the position is poor.
    /// </summary>
    public double? Pdop { get; init; }

    /// <summary>The horizontal dilution of precision: see <see cref="Pdop"/>.</summary>
    public double? Hdop { get; init; }

    /// <summary>The vertical dilution of precision: see <see cref="Pdop"/>.</summary>
    public double? Vdop { get; init; }

    /// <summary>The time dilution of precision (gpsd only): see <see cref="Pdop"/>.</summary>
    public double? Tdop { get; init; }

    /// <summary>The geometric dilution of precision, position and time together (gpsd only): see <see cref="Pdop"/>.</summary>
    public double? Gdop { get; init; }

    /// <summary>
    /// The estimated horizontal error, in metres: gpsd's <c>eph</c>, or from GST the latitude and longitude errors
    /// together (the square root of the sum of their squares).
    /// </summary>
    public double? HorizontalErrorMeters { get; init; }

    /// <summary>The estimated error in latitude, in metres: gpsd's <c>epy</c>, or GST's.</summary>
    public double? LatitudeErrorMeters { get; init; }

    /// <summary>The estimated error in longitude, in metres: gpsd's <c>epx</c>, or GST's.</summary>
    public double? LongitudeErrorMeters { get; init; }

    /// <summary>The estimated error in altitude, in metres: gpsd's <c>epv</c>, or GST's.</summary>
    public double? AltitudeErrorMeters { get; init; }

    /// <summary>The estimated error in speed, in knots: gpsd's <c>eps</c> (NMEA has none).</summary>
    public double? SpeedErrorKnots { get; init; }
}

/// <summary>
/// A satellite a GPS can see: from NMEA's GSV sentences (<see cref="NmeaFixTracker.Satellites"/>) or gpsd's SKY reports.
/// </summary>
/// <remarks>
/// A receiver that tracks more than one signal from a satellite (GPS L1 and L5, say) lists it once for each, told apart
/// by <see cref="SignalId"/>.
/// </remarks>
public sealed record GpsSatellite
{
    /// <summary>The satellite system it belongs to.</summary>
    public GnssSystem System { get; init; }

    /// <summary>
    /// Its number as the source gives it: NMEA's satellite ID (1–32 for GPS, 33–64 for SBAS, 65–96 for GLONASS …), or
    /// gpsd's PRN.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Which of its signals this is, when the source says: NMEA 4.1's signal ID (1 for GPS L1 C/A …) or gpsd's
    /// <c>sigid</c>.
    /// </summary>
    public int? SignalId { get; init; }

    /// <summary>Its elevation above the horizon, in degrees (0–90).</summary>
    public double? ElevationDegrees { get; init; }

    /// <summary>Its azimuth, in degrees from true north (0–359).</summary>
    public double? AzimuthDegrees { get; init; }

    /// <summary>
    /// Its signal-to-noise ratio, in dB-Hz; <see langword="null"/> when the GPS can see it but isn't tracking it.
    /// </summary>
    public double? SnrDbHz { get; init; }

    /// <summary>Whether the fix uses it.</summary>
    public bool Used { get; init; }
}

/// <summary>A satellite navigation system.</summary>
public enum GnssSystem
{
    /// <summary>Not known: the source didn't say, and the satellite's number doesn't tell.</summary>
    Unknown,

    /// <summary>GPS (United States).</summary>
    Gps,

    /// <summary>SBAS: WAAS, EGNOS, MSAS and the like, which correct GPS.</summary>
    Sbas,

    /// <summary>GLONASS (Russia).</summary>
    Glonass,

    /// <summary>Galileo (European Union).</summary>
    Galileo,

    /// <summary>BeiDou (China).</summary>
    BeiDou,

    /// <summary>QZSS (Japan).</summary>
    Qzss,

    /// <summary>NavIC, also called IRNSS (India).</summary>
    NavIC,
}
