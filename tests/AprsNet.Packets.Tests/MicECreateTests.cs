using System.Text;

namespace AprsNet.Packets.Tests;

public class MicECreateTests
{
    private static readonly Callsign _source = Callsign.Parse("N0CALL-9");
    private static readonly Callsign _tocall = Callsign.Parse("APZ123");

    // 33°25.64'N 112°07.74'W, the latitude of the APRS101 ch. 10 destination example.
    private static readonly MicEReport _jeep = new()
    {
        Position = new AprsPosition(33 + (25.64 / 60), -(112 + (7.74 / 60))),
        Symbol = new AprsSymbol('/', 'j'),
        Message = MicEMessage.Returning,
        IsCurrentFix = true,
        IsMessagingCapable = true,
        Course = 251,
        SpeedKnots = 20,
        Comment = "Mobile",
    };

    private static AprsPacket Create(MicEReport report) => AprsPacket.Create(_source, _tocall, [], report);

    private static string Information(AprsPacket packet) => Encoding.Latin1.GetString(packet.Information.Span);

    [Fact]
    public void WritesTheLatitudeMessageAndFlagsInTheDestination()
    {
        var packet = Create(_jeep);

        // S32 = message bits 100 (M3 Returning), U = 5 and north, V = 6 and the +100° offset, T = 4 and west.
        Assert.Equal("S32UVT", packet.Destination!.Value);
    }

    [Fact]
    public void WritesTheLongitudeSpeedCourseAndSymbol()
    {
        var packet = Create(_jeep);

        Assert.Equal("`(_fn\"Oj/`Mobile", Information(packet));
        Assert.Equal("N0CALL-9>S32UVT:`(_fn\"Oj/`Mobile", packet.ToString());
    }

    [Fact]
    public void TheResultDecodesToTheSameValues()
    {
        var decoded = Assert.IsType<MicEReport>(Create(_jeep).Payload);

        Assert.Equal(MicEMessage.Returning, decoded.Message);
        Assert.True(decoded.IsCurrentFix);
        Assert.True(decoded.IsMessagingCapable);
        Assert.Equal(251, decoded.Course);
        Assert.Equal(20, decoded.SpeedKnots);
        Assert.Equal("Mobile", decoded.Comment);
        Assert.Null(decoded.Device);
        Assert.Equal(_jeep.Position.Latitude, decoded.Position.Latitude, 1e-9);
        Assert.Equal(_jeep.Position.Longitude, decoded.Position.Longitude, 1e-9);
    }

    [Fact]
    public void AStationThatCannotReceiveMessagesSendsASpaceTypeByte()
    {
        var packet = Create(
            new MicEReport
            {
                Position = _jeep.Position,
                Symbol = _jeep.Symbol,
                Comment = "X",
            }
        );

        Assert.EndsWith("j/ X", Information(packet), StringComparison.Ordinal);
        Assert.StartsWith("'", Information(packet), StringComparison.Ordinal); // not a current fix
        Assert.False(((MicEReport)packet.Payload!).IsMessagingCapable);
    }

    [Theory]
    [InlineData(MicEMessage.OffDuty, "SSR")] // standard bits 111
    [InlineData(MicEMessage.Priority, "33R")] // standard bits 001
    [InlineData(MicEMessage.Custom0, "DDC")] // custom bits 111
    [InlineData(MicEMessage.Custom2, "D3C")] // custom bits 101
    [InlineData(MicEMessage.Emergency, "332")] // no bits
    public void EncodesEachMessageInTheFirstThreeCharacters(MicEMessage message, string first3)
    {
        // Latitude 33°24': each character is the digit (0-9), the digit with a standard bit (P-Y) or with a custom
        // bit (A-J).
        var report = new MicEReport
        {
            Position = new AprsPosition(33.4, -112),
            Symbol = _jeep.Symbol,
            Message = message,
        };

        var packet = Create(report);

        Assert.Equal(first3, packet.Destination!.Value[..3]);
        Assert.Equal(message, ((MicEReport)packet.Payload!).Message);
    }

    [Theory]
    [InlineData(-33.8688, 151.2093)] // south, east, +100° offset
    [InlineData(51.5072, -0.1276)] // 0-9° west: +100° offset
    [InlineData(64.1466, -21.9426)] // 10-99°: no offset
    [InlineData(-0.5, 109.99)] // 100-109°
    [InlineData(0, 0)]
    public void PositionsInEveryLongitudeRangeRoundTrip(double latitude, double longitude)
    {
        var report = new MicEReport { Position = new AprsPosition(latitude, longitude), Symbol = _jeep.Symbol };

        var decoded = (MicEReport)Create(report).Payload!;

        Assert.Equal(latitude, decoded.Position.Latitude, 0.5 / 6000);
        Assert.Equal(longitude, decoded.Position.Longitude, 0.5 / 6000);
    }

    [Fact]
    public void AmbiguityBlanksTheSameDigitsInTheDestinationAndLongitude()
    {
        var report = new MicEReport
        {
            Position = new AprsPosition(33 + (25.0 / 60), -(112 + (7.0 / 60)), Ambiguity: 2),
            Symbol = _jeep.Symbol,
        };

        var packet = Create(report);

        // OffDuty (SSR), 5 and north (U), then the blanked digits with the offset and west flags set (Z).
        Assert.Equal("SSRUZZ", packet.Destination!.Value);
        Assert.Equal(2, ((MicEReport)packet.Payload!).Position.Ambiguity);
        Assert.Equal((byte)28, packet.Information.Span[3]); // hundredths blanked to zero
    }

    [Fact]
    public void WritesTheAltitudeAsBase91Meters()
    {
        var packet = Create(
            new MicEReport
            {
                Position = _jeep.Position,
                Symbol = _jeep.Symbol,
                AltitudeFeet = 1000,
            }
        );

        // 304.8 m + 10 000 = 10 305 = 1·91² + 22·91 + 22.
        Assert.EndsWith(" \"77}", Information(packet), StringComparison.Ordinal);
        Assert.Equal(1000, ((MicEReport)packet.Payload!).AltitudeFeet!.Value, 0.5 / 0.3048);
    }

    [Fact]
    public void CourseAndSpeedDefaultToZero()
    {
        var decoded = (MicEReport)Create(new MicEReport { Position = _jeep.Position, Symbol = _jeep.Symbol }).Payload!;

        Assert.Equal(0, decoded.Course);
        Assert.Equal(0, decoded.SpeedKnots);
    }

    [Fact]
    public void KeepsTheDestinationSsidAsThePathCode()
    {
        var packet = AprsPacket.Create(_source, Callsign.Parse("APZ123-2"), [], _jeep);

        Assert.Equal("S32UVT-2", packet.Destination!.Value);
    }

    [Fact]
    public void PacketAddressReplacesItsTocall()
    {
        var address = PacketAddress.Parse("N0CALL-9", "APZ123", ["WIDE1-1"]);

        Assert.Equal("N0CALL-9>S32UVT,WIDE1-1:`(_fn\"Oj/`Mobile", address.Create(_jeep).ToString());
    }

    [Fact]
    public void AParsedReportRebuildsByteForByte()
    {
        var packet = AprsPacket.Parse("N0CALL>S32U6T-3,WIDE1-1:`(_fn\"Oj/]\"3x}Mobile=");

        var rebuilt = AprsPacket.Create(packet.Source!, packet.Destination!, packet.Path!, packet.Payload!);

        Assert.Equal(packet.ToString(), rebuilt.ToString());
    }

    [Fact]
    public void RefusesAnUnknownMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Create(
                new MicEReport
                {
                    Position = _jeep.Position,
                    Symbol = _jeep.Symbol,
                    Message = MicEMessage.Unknown,
                }
            )
        );

        Assert.Contains("standard or custom message", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(671.0)]
    [InlineData(-1.0)]
    public void RefusesSpeedsTheFormatCannotCarry(double speed)
    {
        Assert.Throws<ArgumentException>(() =>
            Create(
                new MicEReport
                {
                    Position = _jeep.Position,
                    Symbol = _jeep.Symbol,
                    SpeedKnots = speed,
                }
            )
        );
    }

    [Fact]
    public void RefusesFieldsMicECannotCarry()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(
                new MicEReport
                {
                    Position = _jeep.Position,
                    Symbol = _jeep.Symbol,
                    Timestamp = AprsTimestamp.DayHourMinute(9, 23, 45),
                }
            )
        );
        Assert.Throws<ArgumentException>(() =>
            Create(
                new MicEReport
                {
                    Position = _jeep.Position,
                    Symbol = _jeep.Symbol,
                    PowerHeightGain = new PowerHeightGain(25, 20, 3, null),
                }
            )
        );
    }

    [Fact]
    public void RefusesACommentThatWouldBeReadAsADeviceCode()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Create(
                new MicEReport
                {
                    Position = _jeep.Position,
                    Symbol = _jeep.Symbol,
                    IsMessagingCapable = true,
                    Comment = "Tracker X",
                }
            )
        );

        Assert.Contains("AP510 device code", ex.Message, StringComparison.Ordinal);
    }
}
