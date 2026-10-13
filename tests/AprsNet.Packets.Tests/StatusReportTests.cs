namespace AprsNet.Packets.Tests;

public class StatusReportTests
{
    private static StatusReport Decode(string information, out AprsPacket packet)
    {
        packet = AprsPacket.Parse("N0CALL>APRS:" + information);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        return Assert.IsType<StatusReport>(packet.Payload);
    }

    private static StatusReport Decode(string information) => Decode(information, out _);

    [Fact]
    public void PlainTextIsTheWholeReport()
    {
        var status = Decode(">Net Control Center");

        Assert.Equal("Net Control Center", status.Text);
        Assert.Equal("Net Control Center"u8.ToArray(), status.TextBytes.ToArray());
        Assert.Null(status.Timestamp);
        Assert.Null(status.MaidenheadLocator);
        Assert.Null(status.Symbol);
        Assert.Null(status.BeamHeading);
    }

    [Fact]
    public void AnEmptyStatusIsValid()
    {
        Assert.Equal("", Decode(">").Text);
    }

    [Fact]
    public void ReadsAZuluTimestamp()
    {
        var status = Decode(">092345zNet Control");

        Assert.Equal("092345z", status.Timestamp.ToString());
        Assert.Equal("Net Control", status.Text);
    }

    [Theory]
    [InlineData(">092345/Net Control")]
    [InlineData(">234517hNet Control")]
    [InlineData(">09234zNet Control")]
    public void OtherTimestampFormsAreText(string information)
    {
        var status = Decode(information, out var packet);

        Assert.Null(status.Timestamp);
        Assert.Equal(information[1..], status.Text);
        Assert.Empty(packet.Issues);
    }

    [Fact]
    public void AnImpossibleTimestampStaysTextWithAWarning()
    {
        var status = Decode(">322345zNet Control", out var packet);

        Assert.Null(status.Timestamp);
        Assert.Equal("322345zNet Control", status.Text);
        Assert.Equal([new ParseIssue(ParseIssueCode.InvalidTimestamp, ParseIssueSeverity.Warning, 13)], packet.Issues);
    }

    [Theory]
    [InlineData(">IO91SX/G", "IO91SX", "/G", "")]
    [InlineData(">IO91/G", "IO91", "/G", "")]
    [InlineData(">io91sx/G", "IO91SX", "/G", "")]
    [InlineData(">JN58td\\- At home", "JN58TD", "\\-", " At home")]
    [InlineData(">FN31pr3# digi", "FN31PR", "3#", " digi")]
    public void ReadsAMaidenheadLocatorAndSymbol(string information, string locator, string symbol, string text)
    {
        var status = Decode(information);

        Assert.Equal(locator, status.MaidenheadLocator);
        Assert.Equal(symbol, status.Symbol.ToString());
        Assert.Equal(text, status.Text);
    }

    [Theory]
    [InlineData(">IO91SXG")]
    [InlineData(">IO91SX/GText")]
    [InlineData(">SX91IO/G")]
    [InlineData(">IO9ASX/G")]
    [InlineData(">IO91YX/G")]
    [InlineData(">IO91SX&G")]
    [InlineData(">Hello world")]
    public void TextThatIsNotALocatorStaysText(string information)
    {
        var status = Decode(information);

        Assert.Null(status.MaidenheadLocator);
        Assert.Equal(information[1..], status.Text);
    }

    [Theory]
    [InlineData(">IO91SX/- ^B7", 110, 490)]
    [InlineData(">Meteor scatter ^05", 0, 250)]
    [InlineData(">Meteor scatter ^ZK", 350, 7290)]
    [InlineData(">^9:", 90, 1000)]
    public void ReadsBeamHeadingAndPower(string information, int heading, int watts)
    {
        var status = Decode(information);

        Assert.Equal(heading, status.BeamHeading);
        Assert.Equal(watts, status.EffectiveRadiatedPowerWatts);
        Assert.EndsWith(information[^3..], status.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(">Up ^a7")]
    [InlineData(">Up ^B0")]
    [InlineData(">Up ^BL")]
    [InlineData(">Up B7")]
    [InlineData(">^B")]
    public void NoBeamCodeWithoutAValidTrailingCode(string information)
    {
        Assert.Null(Decode(information).BeamHeading);
    }

    [Fact]
    public void TextKeepsUtf8()
    {
        var packet = AprsPacket.Parse([.. "N0CALL>APRS:>"u8, .. "Ciallo～(∠·ω< )⌒☆"u8]);

        Assert.Equal("Ciallo～(∠·ω< )⌒☆", ((StatusReport)packet.Payload!).Text);
    }
}
