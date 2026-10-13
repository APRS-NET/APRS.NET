using System.Text;

namespace AprsNet.Packets.Tests;

public class AprsPacketTests
{
    [Fact]
    public void ParsesTheHeader()
    {
        var packet = AprsPacket.Parse("N0CALL-9>APRS,WIDE1-1*,WIDE2-1,qAR,N1CALL-10:!4903.50N/07201.75W-Test");

        Assert.True(packet.HasHeader);
        Assert.Equal("N0CALL-9", packet.Source.Value);
        Assert.Equal("APRS", packet.Destination.Value);
        Assert.Equal(
            [
                new PathEntry(packet.Path[0].Station, true, PathEntryKind.Alias),
                new PathEntry(packet.Path[1].Station, false, PathEntryKind.Alias),
                new PathEntry(packet.Path[2].Station, false, PathEntryKind.QConstruct),
                new PathEntry(packet.Path[3].Station, false, PathEntryKind.Station),
            ],
            packet.Path
        );
        Assert.Equal(["WIDE1-1", "WIDE2-1", "qAR", "N1CALL-10"], packet.Path.Select(p => p.Station.Value));
        Assert.Equal("!4903.50N/07201.75W-Test", Encoding.Latin1.GetString(packet.Information.Span));
        Assert.Equal(PacketType.Position, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.IsType<PositionReport>(packet.Payload);
        Assert.Empty(packet.Issues);
    }

    [Fact]
    public void AnEmptyPathIsAnEmptyList()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:>status");

        Assert.True(packet.HasHeader);
        Assert.Empty(packet.Path);
    }

    [Fact]
    public void OnlyTheFirstColonEndsTheHeader()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS::N1CALL   :hello{1");

        Assert.Equal(":N1CALL   :hello{1", Encoding.Latin1.GetString(packet.Information.Span));
        Assert.Equal(PacketType.Message, packet.Type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void DropsOneLineEnding(string ending)
    {
        var line = "N0CALL>APRS:>hi " + ending;

        Assert.Equal("N0CALL>APRS:>hi ", AprsPacket.Parse(line).ToString());
        Assert.Equal("N0CALL>APRS:>hi ", AprsPacket.Parse(Encoding.Latin1.GetBytes(line)).ToString());
    }

    [Fact]
    public void KeepsBytesBeforeTheLastLineEnding()
    {
        Assert.Equal("N0CALL>APRS:>hi\n", AprsPacket.Parse("N0CALL>APRS:>hi\n\n").ToString());
        Assert.Equal("N0CALL>APRS:>hi\r", AprsPacket.Parse("N0CALL>APRS:>hi\r\r\n").ToString());
    }

    [Fact]
    public void KeepsEveryByteFromTheInput()
    {
        // Mic-E data, a degree sign in UTF-8, and a trailing space: none of it is text the parser may change.
        byte[] input = [.. "N0CALL>S32U6T:`(_fn\"Oj/"u8, 0x1C, 0x7F, 0xC2, 0xB0, 0xFF, (byte)' '];

        var packet = AprsPacket.Parse(input);

        Assert.Equal(input, packet.Raw.ToArray());
        Assert.Equal(input, packet.GetBytes());
        Assert.Equal(input, Encoding.Latin1.GetBytes(packet.ToString()));
        Assert.Equal(input, AprsPacket.Parse(packet.ToString()).GetBytes());
    }

    [Fact]
    public void CopiesTheInput()
    {
        var input = Encoding.Latin1.GetBytes("N0CALL>APRS:>one");
        var packet = AprsPacket.Parse(input);

        input[^1] = (byte)'X';
        packet.GetBytes()[^2] = (byte)'X';

        Assert.Equal("N0CALL>APRS:>one", packet.ToString());
    }

    [Fact]
    public void ToStringIsCached()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:>one");

        Assert.Same(packet.ToString(), packet.ToString());
    }

    [Fact]
    public void RejectsANullString()
    {
        Assert.Throws<ArgumentNullException>("line", () => AprsPacket.Parse((string)null!));
    }

    [Fact]
    public void RejectsTextThatWasNotReadAsLatin1()
    {
        var error = Assert.Throws<ArgumentException>("line", () => AprsPacket.Parse("N0CALL>APRS:>Süd ✓"));

        Assert.Contains("U+2713 at index 17", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", ParseIssueCode.MissingHeader, 0)]
    [InlineData("# aprsc 2.1.19", ParseIssueCode.MissingHeader, 0)]
    [InlineData("N0CALL>APRS", ParseIssueCode.MissingHeader, 0)]
    [InlineData("N0CALL:>APRS", ParseIssueCode.MissingHeader, 0)]
    [InlineData(">APRS:>status", ParseIssueCode.EmptySource, 0)]
    public void WithoutAHeaderOnlyTheRawBytesAreKept(string line, ParseIssueCode code, int offset)
    {
        var packet = AprsPacket.Parse(line);

        Assert.False(packet.HasHeader);
        Assert.Null(packet.Source);
        Assert.Null(packet.Destination);
        Assert.Null(packet.Path);
        Assert.True(packet.Information.IsEmpty);
        Assert.Equal(PacketType.Unknown, packet.Type);
        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Equal([new ParseIssue(code, ParseIssueSeverity.Error, offset)], packet.Issues);
        Assert.Equal(line, packet.ToString());
    }

    [Theory]
    [InlineData("N0CALL>:>status", ParseIssueCode.EmptyDestination, 7)]
    [InlineData("N0CALL>,WIDE1-1:>status", ParseIssueCode.EmptyDestination, 7)]
    [InlineData("N0CALL>APRS,,WIDE1-1:>status", ParseIssueCode.EmptyPathEntry, 12)]
    [InlineData("N0CALL>APRS,WIDE1-1,:>status", ParseIssueCode.EmptyPathEntry, 20)]
    [InlineData("N0CALL>APRS,*:>status", ParseIssueCode.EmptyPathEntry, 12)]
    [InlineData("N0CALL>APRS:", ParseIssueCode.EmptyInformation, 12)]
    public void HeaderErrorsMakeThePacketMalformed(string line, ParseIssueCode code, int offset)
    {
        var packet = AprsPacket.Parse(line);

        Assert.True(packet.HasHeader);
        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Equal([new ParseIssue(code, ParseIssueSeverity.Error, offset)], packet.Issues);
    }

    [Fact]
    public void StopsAfterTenPathEntries()
    {
        var line = "N0CALL>APRS," + string.Join(',', Enumerable.Range(1, 12).Select(i => $"D{i}GI")) + ":>status";

        var packet = AprsPacket.Parse(line);

        Assert.Equal(10, packet.Path!.Count);
        Assert.Equal("D10GI", packet.Path[^1].Station.Value);
        Assert.Equal(
            [
                new ParseIssue(
                    ParseIssueCode.TooManyPathEntries,
                    ParseIssueSeverity.Error,
                    line.IndexOf("D11GI", StringComparison.Ordinal)
                ),
            ],
            packet.Issues
        );
        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
    }

    [Fact]
    public void WarnsAboutBytesThatCannotBeInACallsign()
    {
        var packet = AprsPacket.Parse("N0 CALL>APRS,WIDEé1-1:>status");

        Assert.Equal(
            [
                new ParseIssue(ParseIssueCode.InvalidCallsign, ParseIssueSeverity.Warning, 2),
                new ParseIssue(ParseIssueCode.InvalidCallsign, ParseIssueSeverity.Warning, 17),
            ],
            packet.Issues
        );
        Assert.Equal("N0 CALL", packet.Source!.Value);
        Assert.Equal(ParseOutcome.Unsupported, packet.Outcome);
    }

    [Fact]
    public void WarnsAboutAnOverlongInformationField()
    {
        var line = "N0CALL>APRS:>" + new string('x', 600);

        var packet = AprsPacket.Parse(line);

        Assert.Equal(
            [new ParseIssue(ParseIssueCode.InformationTooLong, ParseIssueSeverity.Warning, 12 + 512)],
            packet.Issues
        );
        Assert.Equal(601, packet.Information.Length);
        Assert.Equal(ParseOutcome.Unsupported, packet.Outcome);
    }

    [Theory]
    [InlineData("WIDE1-1", PathEntryKind.Alias)]
    [InlineData("wide2-1", PathEntryKind.Alias)]
    [InlineData("WIDE2", PathEntryKind.Alias)]
    [InlineData("WIDE", PathEntryKind.Alias)]
    [InlineData("TRACE3-3", PathEntryKind.Alias)]
    [InlineData("TEMP1-1", PathEntryKind.Alias)]
    [InlineData("RELAY", PathEntryKind.Alias)]
    [InlineData("ECHO", PathEntryKind.Alias)]
    [InlineData("GATE", PathEntryKind.Alias)]
    [InlineData("NCA2-1", PathEntryKind.Alias)]
    [InlineData("TCPIP", PathEntryKind.Alias)]
    [InlineData("TCPXX", PathEntryKind.Alias)]
    [InlineData("NOGATE", PathEntryKind.Alias)]
    [InlineData("RFONLY", PathEntryKind.Alias)]
    [InlineData("N0CALL-10", PathEntryKind.Station)]
    [InlineData("XDIGI", PathEntryKind.Station)]
    [InlineData("WIDE8-1", PathEntryKind.Station)]
    [InlineData("qAR", PathEntryKind.QConstruct)]
    [InlineData("QAR", PathEntryKind.Station)]
    public void ClassifiesPathEntries(string entry, PathEntryKind kind)
    {
        var packet = AprsPacket.Parse($"N0CALL>APRS,{entry}:>status");

        Assert.Equal(kind, Assert.Single(packet.Path!).Kind);
    }

    [Theory]
    [InlineData("qAC", PathEntryKind.Server)]
    [InlineData("qAX", PathEntryKind.Server)]
    [InlineData("qAU", PathEntryKind.Server)]
    [InlineData("qAI", PathEntryKind.Server)]
    [InlineData("qAR", PathEntryKind.Station)]
    [InlineData("qAO", PathEntryKind.Station)]
    [InlineData("qAS", PathEntryKind.Station)]
    public void TheEntryAfterAQConstructIsAServerOrAStation(string q, PathEntryKind kind)
    {
        var packet = AprsPacket.Parse($"N0CALL>APRS,TCPIP*,{q},T2TEST:>status");

        Assert.Equal([PathEntryKind.Alias, PathEntryKind.QConstruct, kind], packet.Path!.Select(p => p.Kind));
    }

    [Theory]
    [InlineData("!4903.50N/07201.75W-", PacketType.Position)]
    [InlineData("=4903.50N/07201.75W-", PacketType.Position)]
    [InlineData("/092345z4903.50N/07201.75W>", PacketType.Position)]
    [InlineData("@092345z4903.50N/07201.75W>", PacketType.Position)]
    [InlineData("`(_fn\"Oj/", PacketType.MicE)]
    [InlineData("'(_fn\"Oj/", PacketType.MicE)]
    [InlineData("\u001C(_fn\"Oj/", PacketType.MicE)]
    [InlineData("\u001D(_fn\"Oj/", PacketType.MicE)]
    [InlineData(";LEADER   *092345z4903.50N/07201.75W>", PacketType.Object)]
    [InlineData(")AID #2!4903.50N/07201.75WA", PacketType.Item)]
    [InlineData(":N0CALL   :hello", PacketType.Message)]
    [InlineData(">status", PacketType.Status)]
    [InlineData("_10090556c220s004g005t077", PacketType.Weather)]
    [InlineData("T#005,199,000,255,073,123,01101001", PacketType.Telemetry)]
    [InlineData("}N1CALL>APRS,TCPIP,N0CALL*:>status", PacketType.ThirdParty)]
    [InlineData("TEST", PacketType.Unknown)]
    [InlineData("T", PacketType.Unknown)]
    [InlineData(" <UI>:>status", PacketType.Unknown)]
    [InlineData("$GPRMC,063909,A", PacketType.Unknown)]
    public void TheFirstInformationByteGivesTheType(string information, PacketType type)
    {
        Assert.Equal(type, AprsPacket.Parse("N0CALL>APRS:" + information).Type);
    }

    [Fact]
    public void EveryIssueCodeHasADescription()
    {
        foreach (var code in Enum.GetValues<ParseIssueCode>())
        {
            Assert.EndsWith(".", code.Describe(), StringComparison.Ordinal);
        }

        Assert.Equal("99", ((ParseIssueCode)99).Describe());
    }

    [Fact]
    public void PathEntriesPrintAsOnTheWire()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS,N1CALL-1*,WIDE2-1:>status");

        Assert.Equal(["N1CALL-1*", "WIDE2-1"], packet.Path!.Select(p => p.ToString()));
    }
}
