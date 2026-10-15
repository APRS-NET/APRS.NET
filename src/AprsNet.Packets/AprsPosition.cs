namespace AprsNet.Packets;

/// <summary>
/// A position in decimal degrees (WGS84 unless a <c>!DAO!</c> datum says otherwise), north and east positive.
/// </summary>
/// <param name="Latitude">Degrees, −90 to 90.</param>
/// <param name="Longitude">Degrees, −180 to 180.</param>
/// <param name="Ambiguity">
/// How many trailing digits the sender blanked to hide the exact position, 0 to 4 (APRS101 ch. 6): 1 hides the last
/// digit of the hundredths of a minute, 4 hides the minutes. The coordinates keep the blanked digits as zeros, which is
/// the corner of the ambiguity box nearest the equator and the prime meridian in the sender's hemisphere.
/// </param>
public readonly record struct AprsPosition(double Latitude, double Longitude, int Ambiguity = 0)
{
    /// <summary>
    /// The area the sender's position may be anywhere in, given its <see cref="Ambiguity"/>: 0.1′, 1′, 10′ or 1° of
    /// latitude by the same of longitude for ambiguity 1 to 4, or just this point when there is none. Its
    /// <see cref="AprsBounds.Center"/> is the best single position to show.
    /// </summary>
    /// <remarks>
    /// The coordinates hold the blanked digits as zeros, so the box extends away from the equator and the prime
    /// meridian: north and east in the northern and eastern hemispheres, south and west in the others. It is clipped at
    /// the poles and at 180°. A method rather than a property, so that the record's <c>ToString</c> does not print it:
    /// its corners are positions too.
    /// </remarks>
    public AprsBounds GetAmbiguityBounds()
    {
        var size = Ambiguity switch
        {
            1 => 0.1 / 60,
            2 => 1.0 / 60,
            3 => 10.0 / 60,
            4 => 1.0,
            _ => 0,
        };
        var (south, north) = double.IsNegative(Latitude)
            ? (Math.Max(-90, Latitude - size), Latitude)
            : (Latitude, Math.Min(90, Latitude + size));
        var (west, east) = double.IsNegative(Longitude)
            ? (Math.Max(-180, Longitude - size), Longitude)
            : (Longitude, Math.Min(180, Longitude + size));
        return new AprsBounds(new AprsPosition(south, west), new AprsPosition(north, east));
    }
}
