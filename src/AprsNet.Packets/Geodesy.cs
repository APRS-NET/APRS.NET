namespace AprsNet.Packets;

/// <summary>
/// Distances, bearings and points on the Earth's surface, for positions in decimal degrees, and the radio horizon of an
/// antenna.
/// </summary>
/// <remarks>
/// The Earth is taken as a sphere of mean radius <see cref="EarthRadiusKm"/>, as APRS-IS servers measure ranges for
/// their filters. Against the WGS84 ellipsoid a spherical distance is off by at most about 0.5% (less than 0.3% for most
/// paths), which is far below the precision of an APRS position report over the distances APRS covers. Longitudes in
/// results are normalised to −180° (inclusive) to 180° (exclusive); positions in results have no ambiguity.
/// </remarks>
public static class Geodesy
{
    /// <summary>The Earth's mean radius in kilometres used by every calculation here: 6371 km.</summary>
    public const double EarthRadiusKm = 6371.0;

    private static readonly string[] _compassPoints =
    [
        "N",
        "NNE",
        "NE",
        "ENE",
        "E",
        "ESE",
        "SE",
        "SSE",
        "S",
        "SSW",
        "SW",
        "WSW",
        "W",
        "WNW",
        "NW",
        "NNW",
    ];

    /// <summary>The great-circle distance between two positions, in kilometres (the haversine formula).</summary>
    public static double DistanceKm(AprsPosition from, AprsPosition to) => CentralAngle(from, to) * EarthRadiusKm;

    /// <summary>
    /// The initial bearing from <paramref name="from"/> to <paramref name="to"/> along the great circle, in degrees
    /// clockwise from true north, 0 to under 360.
    /// </summary>
    /// <remarks>
    /// The bearing changes along the way except on a meridian or the equator. It is 0 when the positions are the same;
    /// from the North Pole every direction is south (180), and from the South Pole north (0). Towards a pole it is
    /// exactly 0 or 180, whatever the longitudes.
    /// </remarks>
    public static double InitialBearing(AprsPosition from, AprsPosition to)
    {
        if (from.Latitude >= 90)
        {
            return to.Latitude >= 90 ? 0 : 180;
        }

        if (from.Latitude <= -90 || to.Latitude >= 90)
        {
            return 0;
        }

        if (to.Latitude <= -90)
        {
            return 180;
        }

        var lat1 = double.DegreesToRadians(from.Latitude);
        var lat2 = double.DegreesToRadians(to.Latitude);
        var dLon = double.DegreesToRadians(to.Longitude - from.Longitude);
        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = (Math.Cos(lat1) * Math.Sin(lat2)) - (Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon));
        return x == 0 && y == 0 ? 0 : NormalizeBearing(double.RadiansToDegrees(Math.Atan2(y, x)));
    }

    /// <summary>
    /// The position reached by travelling <paramref name="distanceKm"/> from <paramref name="start"/> along the great
    /// circle that leaves it at <paramref name="bearing"/> degrees from true north.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The distance is negative, or either value is not finite.</exception>
    public static AprsPosition Destination(AprsPosition start, double bearing, double distanceKm)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(distanceKm);
        if (!double.IsFinite(distanceKm))
        {
            throw new ArgumentOutOfRangeException(nameof(distanceKm), distanceKm, "The distance must be finite.");
        }

        if (!double.IsFinite(bearing))
        {
            throw new ArgumentOutOfRangeException(nameof(bearing), bearing, "The bearing must be finite.");
        }

        var lat1 = double.DegreesToRadians(start.Latitude);
        var lon1 = double.DegreesToRadians(start.Longitude);
        var course = double.DegreesToRadians(bearing);
        var angle = distanceKm / EarthRadiusKm;
        var sinLat2 = (Math.Sin(lat1) * Math.Cos(angle)) + (Math.Cos(lat1) * Math.Sin(angle) * Math.Cos(course));
        var lat2 = Math.Asin(Math.Clamp(sinLat2, -1, 1));
        var lon2 =
            lon1
            + Math.Atan2(
                Math.Sin(course) * Math.Sin(angle) * Math.Cos(lat1),
                Math.Cos(angle) - (Math.Sin(lat1) * sinLat2)
            );
        return new AprsPosition(double.RadiansToDegrees(lat2), NormalizeLongitude(double.RadiansToDegrees(lon2)));
    }

    /// <summary>
    /// The position a <paramref name="fraction"/> of the way from <paramref name="from"/> to <paramref name="to"/>
    /// along the shorter great circle between them: 0 gives <paramref name="from"/> and 1 gives <paramref name="to"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The fraction is not between 0 and 1.</exception>
    /// <exception cref="ArgumentException">
    /// The positions are antipodal (on opposite sides of the Earth), so no single great circle joins them.
    /// </exception>
    public static AprsPosition Interpolate(AprsPosition from, AprsPosition to, double fraction)
    {
        if (fraction is not (>= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "The fraction must be from 0 to 1.");
        }

        // As unit vectors from the Earth's centre: unlike the haversine, the cross and dot products keep their
        // precision for points nearly opposite each other, where the antipodal test needs it.
        var (x1, y1, z1) = UnitVector(from);
        var (x2, y2, z2) = UnitVector(to);
        var (cx, cy, cz) = ((y1 * z2) - (z1 * y2), (z1 * x2) - (x1 * z2), (x1 * y2) - (y1 * x2));
        var sinAngle = Math.Sqrt((cx * cx) + (cy * cy) + (cz * cz));
        var cosAngle = (x1 * x2) + (y1 * y2) + (z1 * z2);
        if (sinAngle < 1e-12)
        {
            return cosAngle > 0
                ? new AprsPosition(from.Latitude, from.Longitude)
                : throw new ArgumentException(
                    "The positions are antipodal, so every great circle through one passes through the other.",
                    nameof(to)
                );
        }

        var angle = Math.Atan2(sinAngle, cosAngle);
        var a = Math.Sin((1 - fraction) * angle) / sinAngle;
        var b = Math.Sin(fraction * angle) / sinAngle;
        var x = (a * x1) + (b * x2);
        var y = (a * y1) + (b * y2);
        var z = (a * z1) + (b * z2);
        return new AprsPosition(
            double.RadiansToDegrees(Math.Atan2(z, Math.Sqrt((x * x) + (y * y)))),
            NormalizeLongitude(double.RadiansToDegrees(Math.Atan2(y, x)))
        );
    }

    /// <summary>
    /// <paramref name="segments"/> + 1 evenly spaced positions along the shorter great circle from
    /// <paramref name="from"/> to <paramref name="to"/>, both included: a line to draw on a map. Where the line crosses
    /// the antimeridian, consecutive longitudes jump between about 180 and −180.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="segments"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">The positions are antipodal.</exception>
    public static AprsPosition[] PointsAlong(AprsPosition from, AprsPosition to, int segments)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 1);
        var points = new AprsPosition[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            points[i] = Interpolate(from, to, (double)i / segments);
        }

        return points;
    }

    /// <summary>
    /// The nearest compass point to <paramref name="bearing"/> degrees: one of 4 (<c>N</c>, <c>E</c>, <c>S</c>,
    /// <c>W</c>), 8 (adding <c>NE</c> and the like) or 16 (adding <c>NNE</c> and the like). A bearing exactly halfway
    /// between two points takes the clockwise one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="points"/> is not 4, 8 or 16, or the bearing is not finite.
    /// </exception>
    public static string CompassPoint(double bearing, int points = 16)
    {
        if (points is not (4 or 8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(points), points, "A compass has 4, 8 or 16 points here.");
        }

        if (!double.IsFinite(bearing))
        {
            throw new ArgumentOutOfRangeException(nameof(bearing), bearing, "The bearing must be finite.");
        }

        var sector = (int)Math.Floor((NormalizeBearing(bearing) / (360.0 / points)) + 0.5) % points;
        return _compassPoints[sector * (16 / points)];
    }

    /// <summary>
    /// The distance in miles to the radio horizon of an antenna <paramref name="heightFeet"/> above flat terrain:
    /// √(2 × height), the rule of thumb behind APRS ranges. For the range a station's <c>PHG</c> implies, see
    /// <see cref="PowerHeightGain.RangeMiles"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The height is negative or not finite.</exception>
    public static double RadioHorizonMiles(double heightFeet)
    {
        if (heightFeet is not >= 0 || double.IsPositiveInfinity(heightFeet))
        {
            throw new ArgumentOutOfRangeException(
                nameof(heightFeet),
                heightFeet,
                "The height must be zero or more, and finite."
            );
        }

        return Math.Sqrt(2 * heightFeet);
    }

    /// <summary>The angle between two positions seen from the Earth's centre, in radians (haversine).</summary>
    private static double CentralAngle(AprsPosition from, AprsPosition to)
    {
        var lat1 = double.DegreesToRadians(from.Latitude);
        var lat2 = double.DegreesToRadians(to.Latitude);
        var dLat = lat2 - lat1;
        var dLon = double.DegreesToRadians(to.Longitude - from.Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2));
        return 2 * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static (double X, double Y, double Z) UnitVector(AprsPosition position)
    {
        var latitude = double.DegreesToRadians(position.Latitude);
        var longitude = double.DegreesToRadians(position.Longitude);
        return (Math.Cos(latitude) * Math.Cos(longitude), Math.Cos(latitude) * Math.Sin(longitude), Math.Sin(latitude));
    }

    private static double NormalizeBearing(double degrees)
    {
        var normalized = degrees % 360;
        normalized = normalized < 0 ? normalized + 360 : normalized;
        return normalized >= 360 ? 0 : normalized;
    }

    private static double NormalizeLongitude(double degrees)
    {
        var normalized = (degrees + 180) % 360;
        normalized = normalized < 0 ? normalized + 360 : normalized;
        return (normalized >= 360 ? 0 : normalized) - 180;
    }
}
