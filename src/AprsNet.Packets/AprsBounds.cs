namespace AprsNet.Packets;

/// <summary>
/// An area between two parallels and two meridians, given by its south-west and north-east corners, that does not
/// cross the antimeridian.
/// </summary>
/// <param name="SouthWest">The corner with the lowest latitude and longitude.</param>
/// <param name="NorthEast">The corner with the highest latitude and longitude.</param>
public readonly record struct AprsBounds(AprsPosition SouthWest, AprsPosition NorthEast)
{
    /// <summary>The point midway between the corners in latitude and in longitude.</summary>
    public AprsPosition Center =>
        new((SouthWest.Latitude + NorthEast.Latitude) / 2, (SouthWest.Longitude + NorthEast.Longitude) / 2);

    /// <summary>Whether <paramref name="position"/> lies inside the area or on its edge.</summary>
    public bool Contains(AprsPosition position) =>
        position.Latitude >= SouthWest.Latitude
        && position.Latitude <= NorthEast.Latitude
        && position.Longitude >= SouthWest.Longitude
        && position.Longitude <= NorthEast.Longitude;
}
