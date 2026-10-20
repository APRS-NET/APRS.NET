namespace AprsNet.Packets.Tests;

public class NmeaSentenceTests
{
    private static NmeaSentence Parse(string text)
    {
        Assert.True(NmeaSentence.TryParse(text, out var sentence));
        return sentence;
    }

    [Fact]
    public void AnRmcGivesTheDateTimePositionSpeedAndCourse()
    {
        var rmc = Parse("$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A");

        Assert.Equal("GP", rmc.Talker);
        Assert.Equal("RMC", rmc.Type);
        Assert.Equal(11, rmc.Fields.Count);
        Assert.Equal((byte)0x6A, rmc.Checksum);
        Assert.True(rmc.IsChecksumValid);
        Assert.True(rmc.HasFix);
        Assert.Empty(rmc.Issues);
        Assert.Equal(GpsFixMode.Fix, rmc.Fix.Mode);
        Assert.Equal(48.1173, rmc.Fix.Position!.Value.Latitude, 0.000001);
        Assert.Equal(11.516667, rmc.Fix.Position!.Value.Longitude, 0.000001);
        Assert.Equal(22.4, rmc.Fix.SpeedKnots);
        Assert.Equal(84.4, rmc.Fix.Course);
        Assert.Equal(new DateTimeOffset(1994, 3, 23, 12, 35, 19, TimeSpan.Zero), rmc.Fix.Time);
    }

    [Fact]
    public void AGgaGivesTheAltitudeSatellitesAndPrecision()
    {
        var gga = Parse("$GPGGA,102705,5157.9762,N,00029.3256,W,1,04,2.0,75.7,M,47.6,M,,*62");

        Assert.Equal(GpsFixMode.Fix, gga.Fix.Mode);
        Assert.Equal(new AprsPosition(51.96627, -0.48876), Round(gga.Fix.Position));
        Assert.Equal(75.7, gga.Fix.AltitudeMeters);
        Assert.Equal(4, gga.Fix.Satellites);
        Assert.Equal(2.0, gga.Fix.Hdop);
        Assert.Equal(new TimeOnly(10, 27, 5), gga.Fix.UtcTime);
        Assert.Null(gga.Fix.UtcDate);
        Assert.Null(gga.Fix.Time);
    }

    [Fact]
    public void AGllWithoutAChecksumOrStatusStillGivesThePosition()
    {
        var gll = Parse("$GPGLL,2554.459,N,08020.187,W,154027.281,A");

        Assert.Null(gll.Checksum);
        Assert.True(gll.IsChecksumValid);
        Assert.Equal(new AprsPosition(25.90765, -80.33645), Round(gll.Fix.Position));
        Assert.Equal(new TimeOnly(15, 40, 27, 281), gll.Fix.UtcTime);
        Assert.Equal(GpsFixMode.Fix, gll.Fix.Mode);
        Assert.Equal(GpsFixMode.Unknown, Parse("$GPGLL,2554.459,N,08020.187,W").Fix.Mode);
    }

    [Theory]
    [InlineData("$GPVTG,318.7,T,,M,35.1,N,65.0,K*69", 318.7, 35.1)]
    [InlineData("$GPVTG,318.7,T,,M,,N,65.0,K", 318.7, 35.09719)] // km/h only
    [InlineData("$GPVTG,318.7,315.1,35.1,65.0", 318.7, 35.1)] // before NMEA 2.0: no unit letters
    public void AVtgGivesTheCourseAndSpeed(string text, double course, double knots)
    {
        var vtg = Parse(text);

        Assert.Equal(course, vtg.Fix.Course);
        Assert.Equal(knots, vtg.Fix.SpeedKnots!.Value, 0.0001);
        Assert.Null(vtg.Fix.Position);
        Assert.Equal(GpsFixMode.Unknown, vtg.Fix.Mode);
    }

    [Theory]
    [InlineData("$GNGSA,A,3,05,13,15,18,23,24,,,,,,,1.65,0.92,1.37*1D", GpsFixMode.Fix3D, 6, 0.92)]
    [InlineData("$GPGSA,A,2,05,13,15,,,,,,,,,,2.5,2.1,1.4", GpsFixMode.Fix2D, 3, 2.1)]
    [InlineData("$GNGSA,A,1,,,,,,,,,,,,,99.99,99.99,99.99*2E", GpsFixMode.NoFix, 0, 99.99)]
    public void AGsaGivesTheKindOfFix(string text, GpsFixMode mode, int satellites, double hdop)
    {
        var gsa = Parse(text);

        Assert.Equal(mode, gsa.Fix.Mode);
        Assert.Equal(satellites, gsa.Fix.Satellites);
        Assert.Equal(hdop, gsa.Fix.Hdop);
    }

    [Theory]
    [InlineData("GP")]
    [InlineData("GN")]
    [InlineData("GL")]
    [InlineData("GA")]
    [InlineData("GB")]
    [InlineData("BD")]
    public void AnyTalkerGivesAFix(string talker)
    {
        var gga = Parse($"${talker}GGA,102705,5157.9762,N,00029.3256,W,1,04,2.0,75.7,M,47.6,M,,");

        Assert.Equal(talker, gga.Talker);
        Assert.NotNull(gga.Fix.Position);
    }

    [Theory]
    [InlineData("$GPRMC,063909,V,3349.4302,N,11700.3721,W,43.022,89.3,291099,13.6,E")]
    [InlineData("$GPRMC,063909,A,3349.4302,N,11700.3721,W,43.022,89.3,291099,13.6,E,N")]
    [InlineData("$GPGGA,102705,5157.9762,N,00029.3256,W,0,00,99.9,75.7,M,47.6,M,,")]
    [InlineData("$GPGLL,2554.459,N,08020.187,W,154027.281,V")]
    public void ASentenceWithoutAFixKeepsNoPosition(string text)
    {
        var sentence = Parse(text);

        Assert.Equal(GpsFixMode.NoFix, sentence.Fix.Mode);
        Assert.Null(sentence.Fix.Position);
    }

    [Fact]
    public void AWrongChecksumIsAWarningAndTheSentenceIsStillRead()
    {
        var rmc = Parse("$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6B");

        Assert.False(rmc.IsChecksumValid);
        Assert.Equal((byte)0x6B, rmc.Checksum);
        var issue = Assert.Single(rmc.Issues);
        Assert.Equal(new ParseIssue(ParseIssueCode.NmeaChecksumMismatch, ParseIssueSeverity.Warning, 65), issue);
        Assert.NotNull(rmc.Fix.Position);
        Assert.False(Parse("$GPVTG,318.7,T,,M,35.1,N,65.0,K*6").IsChecksumValid); // one digit
    }

    [Theory]
    [InlineData("$GPGGA,1027,5157.9762,N,00029.3256,W,1,04,2.0,75.7,M,47.6,M,,", 7)] // short time
    [InlineData("$GPGGA,102705,5157.9762,X,00029.3256,W,1,04,2.0,75.7,M,47.6,M,,", 14)] // hemisphere
    [InlineData("$GPGGA,102705,5167.9762,N,00029.3256,W,1,04,2.0,75.7,M,47.6,M,,", 14)] // 67 minutes
    [InlineData("$GPGGA,102705,5157.9762,N,00029.3256,W,1,04,2.0,7x.7,M,47.6,M,,", 48)] // altitude
    [InlineData("$GPRMC,063909,A,3349.4302,N,11700.3721,W,43.022,89.3,311199,13.6,E", 53)] // 31 November
    public void AFieldThatCantBeReadIsAWarningAndLeftOut(string text, int offset)
    {
        var sentence = Parse(text);

        var issue = Assert.Single(sentence.Issues);
        Assert.Equal(ParseIssueCode.InvalidNmeaField, issue.Code);
        Assert.Equal(ParseIssueSeverity.Warning, issue.Severity);
        Assert.Equal(offset, issue.Offset);
    }

    [Fact]
    public void OtherSentencesAreKeptWithTheirFields()
    {
        var gsv = Parse("$GPGSV,2,1,07,05,40,083,46,13,62,229,44,15,49,286,42,18,33,051,40*70");
        var garmin = Parse("$PGRMZ,1494,f,3*23");

        Assert.False(gsv.HasFix);
        Assert.Equal(new GpsFix(), gsv.Fix);
        Assert.Equal("07", gsv.Fields[2]);
        Assert.Equal("P", garmin.Talker);
        Assert.Equal("GRMZ", garmin.Type);
        Assert.False(garmin.HasFix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("$")]
    [InlineData("GPRMC,123519,A")]
    [InlineData("$GP")]
    [InlineData("$GP RMC,1")]
    [InlineData("$GPRMC")]
    [InlineData("$ULTW")]
    public void TextThatIsNotASentenceIsRefused(string? text)
    {
        Assert.False(NmeaSentence.TryParse(text, out var sentence));
        Assert.Null(sentence);
    }

    [Fact]
    public void ALineEndingIsIgnored()
    {
        var gga = Parse("$GPGGA,102705,5157.9762,N,00029.3256,W,1,04,2.0,75.7,M,47.6,M,,*62\r\n");

        Assert.True(gga.IsChecksumValid);
        Assert.Equal("$GPGGA,102705,5157.9762,N,00029.3256,W,1,04,2.0,75.7,M,47.6,M,,*62", gga.ToString());
    }

    [Fact]
    public void ParsingNeverThrows()
    {
        var random = new Random(204);
        const string alphabet = "$GPRMCGA,*.0123456789NSEWAVT-";
        for (var i = 0; i < 20_000; i++)
        {
            var text =
                "$"
                + new string([
                    .. Enumerable.Range(0, random.Next(0, 60)).Select(_ => alphabet[random.Next(alphabet.Length)]),
                ]);
            _ = NmeaSentence.TryParse(text, out _);
            _ = NmeaSentence.TryParse("$GPRMC," + text, out _);
        }
    }

    [Fact]
    public void ADollarPacketIsAnNmeaReport()
    {
        var packet = AprsPacket.Parse(
            "N0CALL>APRS:$GPRMC,063909,A,3349.4302,N,11700.3721,W,43.022,89.3,291099,13.6,E*52"
        );

        Assert.Equal(PacketType.Nmea, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        var report = Assert.IsType<NmeaReport>(packet.Payload);
        Assert.Equal("RMC", report.Sentence.Type);
        Assert.Same(report.Sentence.Fix, report.Fix);
        Assert.Equal(43.022, report.Fix.SpeedKnots);
    }

    [Fact]
    public void ADollarPacketsWarningsPointIntoThePacket()
    {
        const string header = "N0CALL>APRS:";
        var packet = AprsPacket.Parse(header + "$GPVTG,318.7,T,,M,35.1,N,65.0,K*68");

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        var issue = Assert.Single(packet.Issues);
        Assert.Equal(ParseIssueCode.NmeaChecksumMismatch, issue.Code);
        Assert.Equal('*', packet.ToString()[issue.Offset]);
    }

    [Theory]
    [InlineData("$")]
    [InlineData("$hello world")]
    [InlineData("$GPRMC")]
    public void ADollarPacketThatIsNotASentenceIsMalformed(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        Assert.Equal(PacketType.Nmea, packet.Type);
        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Contains(packet.Issues, i => i.Code == ParseIssueCode.InvalidNmeaSentence);
        Assert.Null(packet.Payload);
    }

    [Fact]
    public void UltimeterDataIsStillWeather()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:$ULTW0000000001EE4D52----000086A00001----011E015E00000000");

        Assert.Equal(PacketType.Weather, packet.Type);
        Assert.IsType<RawWeatherReport>(packet.Payload);
    }

    private static AprsPosition? Round(AprsPosition? position) =>
        position is { } p ? new AprsPosition(Math.Round(p.Latitude, 5), Math.Round(p.Longitude, 5)) : null;
}
