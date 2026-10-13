namespace AprsNet.Packets.Tests;

public class TelemetryReportTests
{
    private static TelemetryReport Decode(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);
        Assert.Equal(PacketType.Telemetry, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Empty(packet.Issues);
        return Assert.IsType<TelemetryReport>(packet.Payload);
    }

    [Fact]
    public void DecodesTheSpecExample()
    {
        var report = Decode("T#005,199,000,255,073,123,01101001");

        Assert.Equal("005", report.Sequence);
        Assert.Equal(5, report.SequenceNumber);
        Assert.Equal([199, 0, 255, 73, 123], report.Analog);
        Assert.Equal([false, true, true, false, true, false, false, true], report.Digital);
        Assert.Equal("", report.Comment);
    }

    [Theory]
    [InlineData("T#MIC199,000,255,073,123,01101001")]
    [InlineData("T#MIC,199,000,255,073,123,01101001")]
    public void MicHasNoSequenceNumber(string information)
    {
        var report = Decode(information);

        Assert.Equal("MIC", report.Sequence);
        Assert.Null(report.SequenceNumber);
        Assert.Equal(199, report.Analog[0]);
    }

    [Fact]
    public void AcceptsDecimalNegativeAndLargeValues()
    {
        Assert.Equal([13.352, -2.5, 48000, 0.0, 0], Decode("T#598,13.352,-2.5,48000,.0,0").Analog);
    }

    [Theory]
    [InlineData("T#1,5", 1)]
    [InlineData("T#12,5,6,7", 3)]
    [InlineData("T#1234,5,6,7,8,9", 5)]
    public void AcceptsFewerAnalogValuesWithoutDigitalChannels(string information, int count)
    {
        var report = Decode(information);

        Assert.Equal(count, report.Analog.Count);
        Assert.Empty(report.Digital);
    }

    [Fact]
    public void AnEmptyAnalogFieldIsNull()
    {
        Assert.Equal([1, null, 3, null, 5], Decode("T#001,1,,3,,5,00000000").Analog);
    }

    [Fact]
    public void KeepsTheCommentAfterTheDigitalChannels()
    {
        var report = Decode("T#001,1,2,3,4,5,11111111Battery, solar");

        Assert.Equal("Battery, solar", report.Comment);
        Assert.Equal("Battery, solar"u8.ToArray(), report.CommentBytes.ToArray());
        Assert.All(report.Digital, Assert.True);
    }

    [Fact]
    public void ToleratesTrailingSpaces()
    {
        Assert.Equal([1, 2, 3, 4, 5], Decode("T#001,1,2,3,4,5 ").Analog);
    }

    [Theory]
    [InlineData("T#", 14)]
    [InlineData("T#,1,2,3,4,5", 14)]
    [InlineData("T#ABC,1,2,3,4,5", 14)]
    [InlineData("T#0123456789,1", 14)]
    [InlineData("T#001,1,x,3,4,5", 20)]
    [InlineData("T#001,1,2,3,4,NaN", 26)]
    [InlineData("T#001,1,2,3,4,5,0110", 28)]
    [InlineData("T#001,1,2,3,4,5,01102001", 28)]
    [InlineData("T#MIC1,2,3,4,5,hello", 27)]
    public void RejectsUnreadableReports(string information, int offset)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Equal(
            [new ParseIssue(ParseIssueCode.InvalidTelemetry, ParseIssueSeverity.Error, offset)],
            packet.Issues
        );
    }
}
