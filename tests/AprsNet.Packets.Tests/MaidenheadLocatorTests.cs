namespace AprsNet.Packets.Tests;

public class MaidenheadLocatorTests
{
    private const double Tolerance = 1e-9;

    // IARU Region 1 VHF Handbook 10.03 (February 2026), section 20.2.7 "Extreme locators and example test cases for
    // locators", table 20.2.4.1: the cases an implementation "shall be verified" with. The handbook prints the
    // ten-character South Pole locator as "A00AA00AA", one character short; AA00AA00AA is the value its own rules give.
    [Theory]
    [InlineData(-90, 0, "AA00aa", "AA00aa00aa")] // South Pole: longitude is undefined, so its characters are the lowest
    [InlineData(90, 0, "AR09ax", "AR09ax09ax")] // North Pole
    [InlineData(0, 0, "JJ00aa", "JJ00aa00aa")] // Equator, 0° E
    [InlineData(0, 180, "AJ00aa", "AJ00aa00aa")] // Equator, 180° E: the same meridian as 180° W
    [InlineData(-80, -160, "BB00aa", "BB00aa00aa")] // A location in Antarctica
    [InlineData(48.858222, 2.2945, "JN18du", "JN18du55ix")] // Eiffel Tower, Paris
    public void MatchesTheIaruTestCases(double latitude, double longitude, string six, string ten)
    {
        Assert.Equal(six, MaidenheadLocator.FromPosition(latitude, longitude, 6).Value);
        Assert.Equal(ten, MaidenheadLocator.FromPosition(latitude, longitude, 10).Value);
    }

    // The same section, table 20.2.4.2: centroids and squaroid edges for the Eiffel Tower, rounded to 10 decimals.
    [Theory]
    [InlineData("JN18DU", 48.8541666667, 2.2916666667, 48.8333333333, 2.25, 48.875, 2.3333333333)]
    [InlineData("JN18DU55IX", 48.8582465278, 2.2946180556, 48.8581597222, 2.2944444444, 48.8583333333, 2.2947916667)]
    public void MatchesTheIaruCentroidsAndSquaroids(
        string text,
        double centerLatitude,
        double centerLongitude,
        double south,
        double west,
        double north,
        double east
    )
    {
        var locator = MaidenheadLocator.Parse(text);

        Assert.Equal(centerLatitude, locator.Center.Latitude, 1e-10);
        Assert.Equal(centerLongitude, locator.Center.Longitude, 1e-10);
        Assert.Equal(south, locator.SouthWest.Latitude, 1e-10);
        Assert.Equal(west, locator.SouthWest.Longitude, 1e-10);
        Assert.Equal(north, locator.NorthEast.Latitude, 1e-10);
        Assert.Equal(east, locator.NorthEast.Longitude, 1e-10);
    }

    [Fact]
    public void FindsW1awInFn31pr()
    {
        // W1AW, the ARRL's station in Newington, Connecticut: 41°42′53″ N, 72°43′38″ W, grid FN31pr, as given by
        // Wikipedia's "W1AW" article (accessed 2026-10-14).
        var latitude = 41 + (42 / 60.0) + (53 / 3600.0);
        var longitude = -(72 + (43 / 60.0) + (38 / 3600.0));

        Assert.Equal("FN31pr", MaidenheadLocator.FromPosition(latitude, longitude, 6).Value);
        Assert.Equal("FN31", MaidenheadLocator.FromPosition(latitude, longitude, 4).Value);
    }

    [Fact]
    public void GivesTheSquaroidOfEachLength()
    {
        var four = MaidenheadLocator.Parse("FN31");
        var six = MaidenheadLocator.Parse("FN31pr");

        Assert.Equal(new AprsPosition(41, -74), four.SouthWest);
        Assert.Equal(new AprsPosition(42, -72), four.NorthEast);
        Assert.Equal(new AprsPosition(41.5, -73), four.Center);
        Assert.Equal(41 + (42.5 / 60), six.SouthWest.Latitude, Tolerance);
        Assert.Equal(-72.75, six.SouthWest.Longitude, Tolerance);
        Assert.Equal(41.75, six.NorthEast.Latitude, Tolerance);
        Assert.Equal(-72 - (40 / 60.0), six.NorthEast.Longitude, Tolerance);
        Assert.Equal(4, four.Length);
        Assert.Equal(6, six.Length);
    }

    // Round trips

    public static TheoryData<double, double> Positions() =>
        new()
        {
            { 48.858222, 2.2945 },
            { 41.7147, -72.7272 },
            { -33.8568, 151.2153 },
            { -54.8019, -68.3030 },
            { 64.1466, -21.9426 },
            { 0.0001, -0.0001 },
            { 89.9999, 179.9999 },
            { -89.9999, -179.9999 },
            { 12.3456789, 98.7654321 },
        };

    [Theory]
    [MemberData(nameof(Positions))]
    public void RoundTripsAtEveryLength(double latitude, double longitude)
    {
        foreach (var length in new[] { 4, 6, 8, 10 })
        {
            var locator = MaidenheadLocator.FromPosition(latitude, longitude, length);

            Assert.Equal(length, locator.Length);
            Assert.Equal(locator, MaidenheadLocator.Parse(locator.Value));
            Assert.Equal(locator, MaidenheadLocator.FromPosition(locator.Center, length));
            Assert.InRange(latitude, locator.SouthWest.Latitude, locator.NorthEast.Latitude);
            Assert.InRange(longitude, locator.SouthWest.Longitude, locator.NorthEast.Longitude);
        }
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void LongerLocatorsExtendShorterOnes(double latitude, double longitude)
    {
        var ten = MaidenheadLocator.FromPosition(latitude, longitude, 10).Value;

        Assert.Equal(ten[..4], MaidenheadLocator.FromPosition(latitude, longitude, 4).Value);
        Assert.Equal(ten[..6], MaidenheadLocator.FromPosition(latitude, longitude, 6).Value);
        Assert.Equal(ten[..8], MaidenheadLocator.FromPosition(latitude, longitude, 8).Value);
    }

    [Fact]
    public void EveryFourCharacterLocatorRoundTrips()
    {
        for (var field = 0; field < 18 * 18; field++)
        {
            for (var square = 0; square < 100; square++)
            {
                var text = $"{(char)('A' + (field / 18))}{(char)('A' + (field % 18))}{square:00}";

                var locator = MaidenheadLocator.Parse(text);

                Assert.Equal(text, locator.Value);
                Assert.Equal(text, MaidenheadLocator.FromPosition(locator.Center, 4).Value);
                if (locator.SouthWest.Latitude > -90)
                {
                    // A corner on the South Pole reads as AA00: at the poles the longitude characters are the lowest.
                    Assert.Equal(text, MaidenheadLocator.FromPosition(locator.SouthWest, 4).Value);
                }
            }
        }
    }

    // Corners and edges

    [Theory]
    [InlineData(90, 0, "AR09ax09ax")] // the north pole is in the last row, not one past it
    [InlineData(90, 123.4, "AR09ax09ax")] // and its longitude is ignored
    [InlineData(-90, 179, "AA00aa00aa")]
    [InlineData(-90, -180, "AA00aa00aa")]
    [InlineData(0, -180, "AJ00aa00aa")]
    [InlineData(0, 180, "AJ00aa00aa")] // 180° E is 180° W
    [InlineData(89.99999, 179.99999, "RR99xx99xx")] // the last squaroid before the north pole and the antimeridian
    [InlineData(-89.99999, -179.99999, "AA00aa00aa")]
    [InlineData(50, -2, "IO90aa00aa")] // exactly on a corner: the squaroid to its north-east, as in the handbook's IO90
    [InlineData(0, 0, "JJ00aa00aa")]
    public void HandlesCornersAndEdges(double latitude, double longitude, string expected)
    {
        Assert.Equal(expected, MaidenheadLocator.FromPosition(latitude, longitude, 10).Value);
    }

    [Fact]
    public void TheAntimeridianSeparatesTheFirstAndLastFields()
    {
        Assert.StartsWith("R", MaidenheadLocator.FromPosition(10, 179.999, 4).Value, StringComparison.Ordinal);
        Assert.StartsWith("A", MaidenheadLocator.FromPosition(10, -179.999, 4).Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLastSquaroidReachesTheNorthPoleAndTheAntimeridian()
    {
        var locator = MaidenheadLocator.Parse("RR99XX99XX");

        Assert.Equal(90, locator.NorthEast.Latitude, Tolerance);
        Assert.Equal(180, locator.NorthEast.Longitude, Tolerance);
        Assert.Equal(new AprsPosition(-90, -180), MaidenheadLocator.Parse("AA00").SouthWest);
    }

    [Theory]
    [InlineData(-90.0001, 0)]
    [InlineData(90.0001, 0)]
    [InlineData(0, -180.0001)]
    [InlineData(0, 180.0001)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    public void RejectsPositionsOutOfRange(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MaidenheadLocator.FromPosition(latitude, longitude, 6));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(12)]
    public void RejectsOtherLengths(int length)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => MaidenheadLocator.FromPosition(0, 0, length));
        Assert.Equal("length", ex.ParamName);
    }

    // Parsing

    [Theory]
    [InlineData("FN31pr")]
    [InlineData("FN31PR")]
    [InlineData("fn31pr")]
    [InlineData("fN31Pr")]
    [InlineData("  FN31pr\t")]
    public void IgnoresCaseAndSurroundingWhitespace(string text)
    {
        var locator = MaidenheadLocator.Parse(text);

        Assert.Equal("FN31pr", locator.Value);
        Assert.Equal(MaidenheadLocator.Parse("FN31pr"), locator);
    }

    [Fact]
    public void WritesTheCommonForm()
    {
        Assert.Equal("JN18du55ix", MaidenheadLocator.Parse("jn18DU55IX").Value);
        Assert.Equal("JN18du55ix", MaidenheadLocator.Parse("jn18DU55IX").ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReportsEmptyText(string? text)
    {
        Assert.False(MaidenheadLocator.TryParse(text, out var locator, out var error));

        Assert.Null(locator);
        Assert.Equal(MaidenheadParseErrorKind.Empty, error.Kind);
        Assert.Equal(-1, error.Index);
        Assert.Null(error.Part);
        Assert.Equal("Enter a locator, for example FN31pr.", error.Message);
    }

    [Theory]
    [InlineData("F")]
    [InlineData("FN")]
    [InlineData("FN3")]
    [InlineData("FN31p")]
    [InlineData("FN31pr1")]
    [InlineData("FN31pr12a")]
    [InlineData("FN31pr12ab3")]
    [InlineData("FN31pr12ab34cd")]
    public void ReportsTheWrongLength(string text)
    {
        Assert.False(MaidenheadLocator.TryParse(text, out _, out var error));

        Assert.Equal(MaidenheadParseErrorKind.WrongLength, error.Kind);
        Assert.Equal(-1, error.Index);
        Assert.Equal("A locator has 4, 6, 8 or 10 characters.", error.Message);
    }

    [Theory]
    [InlineData("SN31", 0, 'S', MaidenheadLocatorPart.Field, "Character 1 ('S') must be a letter A-R (field).")]
    [InlineData("FS31", 1, 'S', MaidenheadLocatorPart.Field, "Character 2 ('S') must be a letter A-R (field).")]
    [InlineData("1N31", 0, '1', MaidenheadLocatorPart.Field, "Character 1 ('1') must be a letter A-R (field).")]
    [InlineData("FNZ1", 2, 'Z', MaidenheadLocatorPart.Square, "Character 3 ('Z') must be a digit 0-9 (square).")]
    [InlineData("FN3Z", 3, 'Z', MaidenheadLocatorPart.Square, "Character 4 ('Z') must be a digit 0-9 (square).")]
    [InlineData(
        "FN31yr",
        4,
        'y',
        MaidenheadLocatorPart.Subsquare,
        "Character 5 ('y') must be a letter A-X (subsquare)."
    )]
    [InlineData(
        "FN31p5",
        5,
        '5',
        MaidenheadLocatorPart.Subsquare,
        "Character 6 ('5') must be a letter A-X (subsquare)."
    )]
    [InlineData(
        "FN31pra2",
        6,
        'a',
        MaidenheadLocatorPart.ExtendedSquare,
        "Character 7 ('a') must be a digit 0-9 (extended square)."
    )]
    [InlineData(
        "FN31pr12az",
        9,
        'z',
        MaidenheadLocatorPart.ExtendedSubsquare,
        "Character 10 ('z') must be a letter A-X (extended subsquare)."
    )]
    public void ReportsTheCharacterOutOfRange(
        string text,
        int index,
        char character,
        MaidenheadLocatorPart part,
        string message
    )
    {
        Assert.False(MaidenheadLocator.TryParse(text, out var locator, out var error));

        Assert.Null(locator);
        Assert.Equal(MaidenheadParseErrorKind.InvalidCharacter, error.Kind);
        Assert.Equal(index, error.Index);
        Assert.Equal(character, error.Character);
        Assert.Equal(part, error.Part);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void TheIndexPointsIntoTheTextAsGiven()
    {
        Assert.False(MaidenheadLocator.TryParse("  FNZ1", out _, out var error));

        Assert.Equal(4, error.Index); // counting the leading spaces, so a UI can highlight the character
        Assert.Equal("Character 3 ('Z') must be a digit 0-9 (square).", error.Message); // counting what was typed
    }

    [Fact]
    public void ReportsABadCharacterBeforeTheLength()
    {
        // "FNZ" is too short, but the Z is the more useful thing to point out while someone is typing.
        Assert.False(MaidenheadLocator.TryParse("FNZ", out _, out var error));

        Assert.Equal(MaidenheadParseErrorKind.InvalidCharacter, error.Kind);
    }

    [Theory]
    [InlineData("FN 31")]
    [InlineData("FN31\0")]
    [InlineData("ＦＮ31")] // full-width letters
    [InlineData("FN٣١")] // Arabic-Indic digits
    [InlineData("FN31pré")]
    [InlineData("𐀀𐀀")]
    public void NeverThrowsOnMalformedText(string text)
    {
        Assert.False(MaidenheadLocator.TryParse(text, out var locator, out var error));
        Assert.Null(locator);
        Assert.NotEmpty(error.Message);
        Assert.False(MaidenheadLocator.TryParse(text, out locator));
    }

    [Fact]
    public void ParseThrowsWithTheReason()
    {
        var ex = Assert.Throws<FormatException>(() => MaidenheadLocator.Parse("FNZ1"));

        Assert.Equal("Character 3 ('Z') must be a digit 0-9 (square).", ex.Message);
        Assert.Throws<ArgumentNullException>(() => MaidenheadLocator.Parse(null!));
    }

    [Fact]
    public void ReadsTheLocatorsThatStatusReportsCarry()
    {
        var status = Assert.IsType<StatusReport>(AprsPacket.Parse("N0CALL>APRS:>io91sx/G ^B7").Payload);

        var locator = MaidenheadLocator.Parse(status.MaidenheadLocator!);

        Assert.Equal("IO91sx", locator.Value);
    }
}
