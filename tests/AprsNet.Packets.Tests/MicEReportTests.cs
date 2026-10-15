namespace AprsNet.Packets.Tests;

public class MicEReportTests
{
    // Destination T4SQZZ: 44°31' N, ambiguity 2, message In Service, +100° longitude offset, west.
    // Information `(_fn"Oj/: 112°07.74' W, 20 knots, course 251°, symbol /j (jeep).
    private const string _dest = "T4SQZZ";
    private const string _body = "(_fn\"Oj/";

    private static MicEReport Decode(string destination, string information, out AprsPacket packet)
    {
        packet = AprsPacket.Parse($"N0CALL>{destination}:{information}");
        Assert.Equal(PacketType.MicE, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        return Assert.IsType<MicEReport>(packet.Payload);
    }

    private static MicEReport Decode(string information) => Decode(_dest, information, out _);

    [Fact]
    public void DecodesTheSpecExample()
    {
        var report = Decode(_dest, "`" + _body, out var packet);

        Assert.Equal(44.516667, report.Position.Latitude, 6);
        Assert.Equal(-112.116667, report.Position.Longitude, 6);
        Assert.Equal(2, report.Position.Ambiguity);
        Assert.Equal(20, report.SpeedKnots);
        Assert.Equal(251, report.Course);
        Assert.Equal(new AprsSymbol('/', 'j'), report.Symbol);
        Assert.Equal(MicEMessage.InService, report.Message);
        Assert.True(report.IsCurrentFix);
        Assert.False(report.IsMessagingCapable);
        Assert.Null(report.Device);
        Assert.Equal("", report.Comment);
        Assert.Empty(packet.Issues);
    }

    [Fact]
    public void TheDestinationSsidIsIgnored()
    {
        Assert.Equal(44.516667, Decode(_dest + "-2", "`" + _body, out _).Position.Latitude, 6);
    }

    [Theory]
    [InlineData("PPPPPP", MicEMessage.OffDuty)]
    [InlineData("PP0PPP", MicEMessage.EnRoute)]
    [InlineData("P0PPPP", MicEMessage.InService)]
    [InlineData("P00PPP", MicEMessage.Returning)]
    [InlineData("0PPPPP", MicEMessage.Committed)]
    [InlineData("0P0PPP", MicEMessage.Special)]
    [InlineData("00PPPP", MicEMessage.Priority)]
    [InlineData("000PPP", MicEMessage.Emergency)]
    [InlineData("AAAPPP", MicEMessage.Custom0)]
    [InlineData("A0APPP", MicEMessage.Custom2)]
    [InlineData("00APPP", MicEMessage.Custom6)]
    [InlineData("PA0PPP", MicEMessage.Unknown)]
    public void ReadsTheMessageBits(string destination, MicEMessage message)
    {
        Assert.Equal(message, Decode(destination, "`" + _body, out _).Message);
    }

    [Theory]
    [InlineData("T4SQZZ", 2)]
    [InlineData("T4SQPZ", 1)]
    [InlineData("T4SZZZ", 3)]
    [InlineData("T4ZZZZ", 4)]
    [InlineData("T4SQPP", 0)]
    public void AmbiguityBlanksTheSameLongitudeDigits(string destination, int ambiguity)
    {
        var report = Decode(destination, "`" + _body, out _);

        Assert.Equal(ambiguity, report.Position.Ambiguity);
        var expected = ambiguity switch
        {
            0 => 112 + (7.74 / 60),
            1 => 112 + (7.70 / 60),
            2 => 112 + (7.0 / 60),
            _ => 112.0,
        };
        Assert.Equal(-expected, report.Position.Longitude, 6);
    }

    [Theory]
    [InlineData("T4SQ")]
    [InlineData("T4SQZZZ")]
    [InlineData("T4S!ZZ")]
    [InlineData("TZSQPP")]
    [InlineData("9PSQZZ")]
    public void RejectsInvalidDestinations(string destination)
    {
        var packet = AprsPacket.Parse($"N0CALL>{destination}:`{_body}");

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Equal(
            [new ParseIssue(ParseIssueCode.InvalidMicEDestination, ParseIssueSeverity.Error, 7)],
            packet.Issues
        );
    }

    [Theory]
    [InlineData("`\u0080_fn\"Oj/")]
    [InlineData("`(\u001bfn\"Oj/")]
    [InlineData("`(_\u0010n\"Oj/")]
    [InlineData("`(_\u0080n\"Oj/")]
    public void RejectsInvalidLongitudeBytes(string information)
    {
        var packet = AprsPacket.Parse($"N0CALL>{_dest}:{information}");

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Contains(packet.Issues, i => i.Code == ParseIssueCode.InvalidLongitude);
    }

    [Fact]
    public void RejectsAnInvalidSymbolTable()
    {
        var packet = AprsPacket.Parse($"N0CALL>{_dest}:`(_fn\"Oj&");

        Assert.Contains(packet.Issues, i => i.Code == ParseIssueCode.InvalidSymbolTable);
    }

    [Theory]
    [InlineData("`(_fs@z>/", 73, 294)]
    [InlineData("`(_f#<z>/", 73, 294)]
    [InlineData("`(_f\u001c\u001c\u001c>/", 0, 0)]
    public void ReadsSpeedAndCourse(string information, double speed, int course)
    {
        var report = Decode(information);

        Assert.Equal(speed, report.SpeedKnots);
        Assert.Equal(course, report.Course);
    }

    [Fact]
    public void SpeedAboveMachOneUsesTheApRs12Rule()
    {
        // 799 knots encoded: SP = 79 + 28 = 'k', DC = 9 * 10 + 0 + 28 = 'v', SE = 28.
        var report = Decode("`(_fkv\u001c>/");

        Assert.Equal(799 * 112 - 74370, report.SpeedKnots);
    }

    [Fact]
    public void SpeedBytesBelow28MeanNoSpeed()
    {
        var report = Decode("`(_f\u0010\"O>/");

        Assert.Null(report.SpeedKnots);
        Assert.Null(report.Course);
    }

    [Theory]
    [InlineData("`" + _body + ">Hello", "TH-D7", true, "Hello")]
    [InlineData("`" + _body + ">Hello=", "TH-D72", true, "Hello")]
    [InlineData("`" + _body + ">Hello^", "TH-D74", true, "Hello")]
    [InlineData("`" + _body + "]Hello", "TM-D700", true, "Hello")]
    [InlineData("`" + _body + "]Hello=", "TM-D710", true, "Hello")]
    [InlineData("`" + _body + "`Hello _%", "FTM-400DR", true, "Hello ")]
    [InlineData("`" + _body + "`_1", "FTM-300D", true, "")]
    [InlineData("'" + _body + "'Tracking|3", "TinyTrack3", false, "Tracking")]
    [InlineData("`" + _body + "`Hello _9", null, true, "Hello _9")]
    [InlineData("`" + _body + " Original", null, false, "Original")]
    [InlineData("`" + _body + "Plain text", null, false, "Plain text")]
    public void ReadsTheTypeByteAndDevice(string information, string? device, bool messaging, string comment)
    {
        var report = Decode(information);

        Assert.Equal(device, report.Device);
        Assert.Equal(messaging, report.IsMessagingCapable);
        Assert.Equal(comment, report.Comment);
    }

    [Theory]
    [InlineData("`" + _body, true)]
    [InlineData("\u001c" + _body, true)]
    [InlineData("'" + _body, false)]
    [InlineData("\u001d" + _body, false)]
    [InlineData("'" + _body + "]Kenwood sends ' for current data", true)]
    [InlineData("'" + _body + ">Hello", false)]
    public void ReadsTheFixAge(string information, bool current)
    {
        Assert.Equal(current, Decode(information).IsCurrentFix);
    }

    [Fact]
    public void ReadsAltitudeAfterTheTypeByte()
    {
        var report = Decode("`" + _body + "`\"4T}Hello _1");

        Assert.Equal(61, report.AltitudeFeet!.Value * 0.3048, 6);
        Assert.Equal("Hello ", report.Comment);
        Assert.Equal("FTM-300D", report.Device);
    }

    [Fact]
    public void FallsBackToAltitudeInTheComment()
    {
        Assert.Equal(1234, Decode("`" + _body + ">On the road /A=001234").AltitudeFeet);
    }

    [Fact]
    public void AppliesDaoPrecisionToAnUnambiguousPosition()
    {
        var report = Decode("T4SQPP", "`" + _body + ">Hi !W23!", out _);

        Assert.Equal('W', report.Datum);
        Assert.Equal(44 + (31.002 / 60), report.Position.Latitude, 6);
        Assert.Equal("Hi ", report.Comment);
    }

    [Fact]
    public void ReadsALocatorAtTheStartOfTheStatus()
    {
        Assert.Equal("IO91SX", Decode("`" + _body + "IO91SX/G Hello world").MaidenheadLocator);
    }

    [Fact]
    public void KeepsTheWholeTextInCommentBytes()
    {
        var report = Decode("`" + _body + ">Hello=");

        Assert.Equal(">Hello="u8.ToArray(), report.CommentBytes.ToArray());
    }

    [Fact]
    public void MicEIssueCodeHasADescription()
    {
        Assert.EndsWith(".", ParseIssueCode.InvalidMicEDestination.Describe(), StringComparison.Ordinal);
    }
}
