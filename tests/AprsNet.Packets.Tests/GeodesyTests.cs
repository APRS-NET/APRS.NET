namespace AprsNet.Packets.Tests;

public class GeodesyTests
{
    // Ed Williams' Aviation Formulary (edwilliams.org/avform147.htm), the worked example used throughout it: LAX at
    // 33°57′ N 118°24′ W and JFK at 40°38′ N 73°47′ W are 0.623585 radians (2144 nm) apart, and the initial true course
    // from LAX is 66°.
    private static readonly AprsPosition _lax = new(33 + (57 / 60.0), -(118 + (24 / 60.0)));
    private static readonly AprsPosition _jfk = new(40 + (38 / 60.0), -(73 + (47 / 60.0)));

    [Fact]
    public void MatchesTheAviationFormularyExample()
    {
        Assert.Equal(0.623585, Geodesy.DistanceKm(_lax, _jfk) / Geodesy.EarthRadiusKm, tolerance: 1e-6);
        Assert.Equal(66, Geodesy.InitialBearing(_lax, _jfk), 0);
    }

    [Theory]
    [InlineData(0, 0, 1, 0, 111.19)] // one degree of latitude on a 6371 km sphere
    [InlineData(0, 0, 0, 90, 10_007.54)] // a quarter of the way round
    [InlineData(0, 0, 0, 180, 20_015.09)] // half way round
    [InlineData(0, 179, 0, -179, 222.39)] // across the antimeridian: 2°, not 358°
    [InlineData(90, 0, 90, 123, 0)] // the North Pole has every longitude
    [InlineData(90, 0, -90, 0, 20_015.09)] // pole to pole
    [InlineData(89, 0, 89, 180, 222.39)] // over the pole
    public void MeasuresKnownDistances(
        double fromLatitude,
        double fromLongitude,
        double toLatitude,
        double toLongitude,
        double km
    ) => Assert.Equal(km, Geodesy.DistanceKm(new(fromLatitude, fromLongitude), new(toLatitude, toLongitude)), 2);

    [Theory]
    [InlineData(0, 0, 1, 0, 0)]
    [InlineData(0, 0, 0, 1, 90)]
    [InlineData(1, 0, 0, 0, 180)]
    [InlineData(0, 0, 0, -1, 270)]
    [InlineData(0, 179, 0, -179, 90)] // east across the antimeridian
    [InlineData(0, -179, 0, 179, 270)]
    [InlineData(10, 20, 10, 20, 0)] // the same place
    [InlineData(90, 0, 10, 45, 180)] // from the North Pole everything is south
    [InlineData(-90, 0, 10, 45, 0)] // and from the South Pole north
    [InlineData(10, 20, 90, 0, 0)] // to the North Pole is north, whatever the longitude
    [InlineData(10, 20, -90, 0, 180)]
    public void GivesInitialBearings(
        double fromLatitude,
        double fromLongitude,
        double toLatitude,
        double toLongitude,
        double bearing
    ) =>
        Assert.Equal(
            bearing,
            Geodesy.InitialBearing(new(fromLatitude, fromLongitude), new(toLatitude, toLongitude)),
            9
        );

    [Theory]
    [InlineData(0, 0, 90, 10_007.543398, 0, 90)]
    [InlineData(0, 170, 90, 2_223.898, 0, -170)] // across the antimeridian
    [InlineData(80, 0, 0, 2_223.898, 80, -180)] // over the North Pole and down the other side
    [InlineData(-45, 30, 123, 0, -45, 30)] // nowhere
    public void FindsDestinations(
        double latitude,
        double longitude,
        double bearing,
        double km,
        double toLatitude,
        double toLongitude
    )
    {
        var to = Geodesy.Destination(new(latitude, longitude), bearing, km);

        Assert.Equal(toLatitude, to.Latitude, 4);
        Assert.Equal(toLongitude, to.Longitude, 4);
    }

    [Fact]
    public void DestinationUndoesBearingAndDistance()
    {
        var to = Geodesy.Destination(_lax, Geodesy.InitialBearing(_lax, _jfk), Geodesy.DistanceKm(_lax, _jfk));

        Assert.Equal(_jfk.Latitude, to.Latitude, 9);
        Assert.Equal(_jfk.Longitude, to.Longitude, 9);
    }

    [Fact]
    public void DestinationRefusesNegativeOrUnusableValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.Destination(_lax, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.Destination(_lax, 0, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.Destination(_lax, double.NaN, 1));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.4)]
    [InlineData(1.0)]
    public void InterpolatesAlongTheGreatCircle(double fraction)
    {
        var total = Geodesy.DistanceKm(_lax, _jfk);
        var point = Geodesy.Interpolate(_lax, _jfk, fraction);

        Assert.Equal(fraction * total, Geodesy.DistanceKm(_lax, point), 6);
        Assert.Equal((1 - fraction) * total, Geodesy.DistanceKm(point, _jfk), 6);
    }

    [Fact]
    public void InterpolatesAcrossTheAntimeridian()
    {
        var midpoint = Geodesy.Interpolate(new(0, 170), new(0, -170), 0.5);

        Assert.Equal(0, midpoint.Latitude, 9);
        Assert.Equal(180, Math.Abs(midpoint.Longitude), 9);
    }

    [Fact]
    public void InterpolationDropsAmbiguity()
    {
        var from = new AprsPosition(10, 20, 3);

        Assert.Equal(new AprsPosition(10, 20), Geodesy.Interpolate(from, from, 0.5));
    }

    [Fact]
    public void InterpolationRefusesAntipodesAndFractionsOutsideTheLine()
    {
        Assert.Throws<ArgumentException>(() => Geodesy.Interpolate(new(10, 20), new(-10, -160), 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.Interpolate(_lax, _jfk, 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.Interpolate(_lax, _jfk, double.NaN));
    }

    [Fact]
    public void PointsAlongIncludeBothEnds()
    {
        var points = Geodesy.PointsAlong(_lax, _jfk, 4);

        Assert.Equal(5, points.Length);
        Assert.Equal(_lax.Latitude, points[0].Latitude, 9);
        Assert.Equal(_jfk.Longitude, points[4].Longitude, 9);
        Assert.Equal(Geodesy.Interpolate(_lax, _jfk, 0.25), points[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.PointsAlong(_lax, _jfk, 0));
    }

    [Theory]
    [InlineData(0, 16, "N")]
    [InlineData(11.24, 16, "N")]
    [InlineData(11.25, 16, "NNE")] // halfway goes clockwise
    [InlineData(45, 16, "NE")]
    [InlineData(200, 16, "SSW")]
    [InlineData(348.75, 16, "N")]
    [InlineData(359.9, 16, "N")]
    [InlineData(-90, 16, "W")]
    [InlineData(900, 16, "S")]
    [InlineData(22.4, 8, "N")]
    [InlineData(22.5, 8, "NE")]
    [InlineData(292.5, 8, "NW")]
    [InlineData(44.9, 4, "N")]
    [InlineData(135, 4, "S")]
    public void NamesCompassPoints(double bearing, int points, string name) =>
        Assert.Equal(name, Geodesy.CompassPoint(bearing, points));

    [Fact]
    public void CompassPointRefusesOtherCompasses()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.CompassPoint(0, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.CompassPoint(double.NaN));
    }

    [Fact]
    public void RadioHorizonIsTheSquareRootOfTwiceTheHeight()
    {
        Assert.Equal(10, Geodesy.RadioHorizonMiles(50), 9);
        Assert.Equal(0, Geodesy.RadioHorizonMiles(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.RadioHorizonMiles(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.RadioHorizonMiles(double.NaN));
    }

    [Fact]
    public void PhgRangeFollowsTheAprsFormula()
    {
        // APRS101 ch. 7's example PHG5132: 25 W, 20 ft, 3 dB. √(2 × 20 × √(25 / 10 × 10^0.3 / 2)) = 7.95 miles.
        Assert.Equal(7.95, new PowerHeightGain(25, 20, 3, 90).RangeMiles, 2);
    }

    [Theory]
    [InlineData("!49  .  N/072  .  W-", 4, 49, -73, 50, -72)] // 1° by 1°, away from the equator and the meridian
    [InlineData("!33  .  S/151  .  E-", 4, -34, 151, -33, 152)]
    [InlineData("!4903.  N/07201.  W-", 2, 49.05, -72.033333, 49.066667, -72.016667)] // 1′
    [InlineData("!4903.5 N/07201.7 W-", 1, 49.058333, -72.03, 49.06, -72.028333)] // 0.1′
    public void AmbiguityBoundsCoverEveryPositionTheSenderMightBeAt(
        string information,
        int ambiguity,
        double south,
        double west,
        double north,
        double east
    )
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);
        var position = Assert.IsType<PositionReport>(packet.Payload).Position;
        var bounds = position.GetAmbiguityBounds();

        Assert.Equal(ambiguity, position.Ambiguity);
        Assert.Equal(south, bounds.SouthWest.Latitude, 6);
        Assert.Equal(west, bounds.SouthWest.Longitude, 6);
        Assert.Equal(north, bounds.NorthEast.Latitude, 6);
        Assert.Equal(east, bounds.NorthEast.Longitude, 6);
        Assert.True(bounds.Contains(position));
        Assert.True(bounds.Contains(bounds.Center));
    }

    [Fact]
    public void AmbiguityBoundsStopAtThePolesAndTheAntimeridian()
    {
        var bounds = new AprsPosition(89.5, 179.5, 4).GetAmbiguityBounds();

        Assert.Equal(new AprsPosition(90, 180), bounds.NorthEast);
        Assert.Equal(new AprsPosition(-90, -180), new AprsPosition(-89.5, -179.5, 4).GetAmbiguityBounds().SouthWest);
    }

    [Fact]
    public void AnExactPositionIsItsOwnBounds()
    {
        var position = new AprsPosition(49.05, -72.03);
        var bounds = position.GetAmbiguityBounds();

        Assert.Equal(position, bounds.SouthWest);
        Assert.Equal(position, bounds.NorthEast);
        Assert.Equal(position, bounds.Center);
        Assert.False(bounds.Contains(new AprsPosition(49.06, -72.03)));
    }

    [Fact]
    public void PositionsAndBoundsPrintWithoutRecursing()
    {
        var bounds = new AprsPosition(49.05, -72.03, 2).GetAmbiguityBounds();

        // Records print their public properties, so a position that printed its bounds would recurse forever.
        Assert.EndsWith("Ambiguity = 2 }", new AprsPosition(49.05, -72.03, 2).ToString());
        Assert.StartsWith("AprsBounds { SouthWest = AprsPosition {", bounds.ToString());
    }
}
