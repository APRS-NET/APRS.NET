namespace AprsNet.Packets;

/// <summary>
/// Station power, antenna height above average terrain, gain and directivity (the <c>PHGphgd</c> data extension,
/// APRS101 ch. 7), with the radio range they imply.
/// </summary>
/// <param name="PowerWatts">Transmitter power in watts.</param>
/// <param name="HeightFeet">Antenna height above average terrain, in feet.</param>
/// <param name="GainDbi">Antenna gain in dBi (APRS 1.1 erratum A11-ERR-04).</param>
/// <param name="Directivity">Direction of maximum gain in degrees, or <see langword="null"/> for omnidirectional.</param>
public sealed record PowerHeightGain(int PowerWatts, int HeightFeet, int GainDbi, int? Directivity)
{
    /// <summary>
    /// The range in miles APRS derives from these values: √(2 × height × √(power / 10 × gain ratio / 2)).
    /// </summary>
    public double RangeMiles =>
        Math.Sqrt(2 * HeightFeet * Math.Sqrt(PowerWatts / 10.0 * Math.Pow(10, GainDbi / 10.0) / 2));
}

/// <summary>
/// Received signal strength from a direction-finding station, with its antenna details (the <c>DFSshgd</c> data
/// extension, APRS101 ch. 7).
/// </summary>
/// <param name="Strength">Signal strength, 0 to 9 (S-points).</param>
/// <param name="HeightFeet">Antenna height above average terrain, in feet.</param>
/// <param name="GainDbi">Antenna gain in dBi.</param>
/// <param name="Directivity">Direction of maximum gain in degrees, or <see langword="null"/> for omnidirectional.</param>
public sealed record DirectionFindingStrength(int Strength, int HeightFeet, int GainDbi, int? Directivity);

/// <summary>
/// A direction-finding bearing report: the <c>/BRG/NRQ</c> fields that follow course and speed on the DF symbol
/// (<c>/\</c>, APRS101 ch. 7).
/// </summary>
/// <param name="Bearing">Bearing to the signal in degrees.</param>
/// <param name="Hits">How many hits the DF unit got, 0 to 9 (N; 9 means manual).</param>
/// <param name="RangeMiles">How far the bearing line should be drawn, in miles (2 to the power R).</param>
/// <param name="Quality">Bearing accuracy, 0 (useless) to 9 (under 1°) (Q).</param>
/// <param name="IsFixedStation">Whether the DF station is fixed (course 000) rather than mobile.</param>
public sealed record DirectionFindingReport(int Bearing, int Hits, int RangeMiles, int Quality, bool IsFixedStation);

// The compression type enums are set by casting bits of the type byte, so their members look unused to inspectors.
// ReSharper disable UnusedMember.Global

/// <summary>Whether the GPS fix behind a compressed position is current (the compression type byte).</summary>
public enum CompressedGpsFix
{
    /// <summary>An old fix: the last known position.</summary>
    Old,

    /// <summary>A current fix.</summary>
    Current,
}

/// <summary>Which NMEA sentence a compressed position came from (the compression type byte).</summary>
public enum CompressedNmeaSource
{
    /// <summary>Another source, or none.</summary>
    Other,

    /// <summary><c>$GPGLL</c>.</summary>
    Gll,

    /// <summary><c>$GPGGA</c>; the course and speed bytes then hold the altitude.</summary>
    Gga,

    /// <summary><c>$GPRMC</c>.</summary>
    Rmc,
}

/// <summary>What produced a compressed position (the compression type byte).</summary>
public enum CompressedOrigin
{
    /// <summary>Compressed by the sender.</summary>
    Compressed,

    /// <summary>A TNC beacon text.</summary>
    TncBText,

    /// <summary>APRS software (APRSdos, MacAPRS, WinAPRS, APRS+SA and others).</summary>
    Software,

    /// <summary>Reserved.</summary>
    Reserved,

    /// <summary>A KPC-3 TNC.</summary>
    Kpc3,

    /// <summary>A Pico tracker.</summary>
    Pico,

    /// <summary>Another tracker.</summary>
    OtherTracker,

    /// <summary>Converted by a digipeater.</summary>
    DigipeaterConversion,
}

// ReSharper restore UnusedMember.Global

/// <summary>The compression type byte of a compressed position (APRS101 ch. 9).</summary>
/// <param name="GpsFix">Whether the fix is current.</param>
/// <param name="NmeaSource">The NMEA sentence the position came from.</param>
/// <param name="Origin">What produced the position.</param>
public readonly record struct CompressionType(
    CompressedGpsFix GpsFix,
    CompressedNmeaSource NmeaSource,
    CompressedOrigin Origin
);

/// <summary>The shapes an area object can draw (APRS101 ch. 11, corrected by APRS 1.1).</summary>
public enum AreaShapeKind
{
    /// <summary>A circle.</summary>
    Circle,

    /// <summary>A line, drawn down and to the right from the position (type 1), or down and to the left (type 6).</summary>
    Line,

    /// <summary>An ellipse.</summary>
    Ellipse,

    /// <summary>A triangle.</summary>
    Triangle,

    /// <summary>A box.</summary>
    Box,
}

/// <summary>
/// The <c>Tyy/Cxx</c> extension of an area symbol (<c>\l</c>, APRS101 ch. 11): a shape, a colour, and two size codes
/// that APRS 1.1 turns into degrees as code² / 1500 (A11-AREA-01).
/// </summary>
/// <param name="Type">The type digit as sent, 0 to 9: the shape is <c>Type % 5</c>, and 5 and up are colour-filled.</param>
/// <param name="Color">The colour, 0 to 15.</param>
/// <param name="LatitudeCode">The latitude size code (<c>yy</c>).</param>
/// <param name="LongitudeCode">The longitude size code (<c>xx</c>).</param>
/// <param name="CorridorMiles">For a line, the corridor width in miles from <c>{nnn}</c> in the comment.</param>
public sealed record AreaShape(int Type, int Color, int LatitudeCode, int LongitudeCode, int? CorridorMiles)
{
    /// <summary>The shape drawn.</summary>
    public AreaShapeKind Kind => Type == 6 ? AreaShapeKind.Line : (AreaShapeKind)(Type % 5);

    /// <summary>Whether the shape is filled with its colour (types 5 to 9, except the type-6 line).</summary>
    public bool IsFilled => Type >= 5 && Type != 6;

    /// <summary>The latitude extent in degrees: <see cref="LatitudeCode"/>² / 1500.</summary>
    public double LatitudeOffsetDegrees => LatitudeCode * LatitudeCode / 1500.0;

    /// <summary>The longitude extent in degrees: <see cref="LongitudeCode"/>² / 1500.</summary>
    public double LongitudeOffsetDegrees => LongitudeCode * LongitudeCode / 1500.0;
}
