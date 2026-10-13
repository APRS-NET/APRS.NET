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
public readonly record struct AprsPosition(double Latitude, double Longitude, int Ambiguity = 0);
