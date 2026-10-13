using System.Text;

namespace AprsNet.Packets.Tests;

public class PositionReportTests
{
    private static PositionReport Decode(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        return Assert.IsType<PositionReport>(packet.Payload);
    }

    private static void Fail(string information, ParseIssueCode code, int informationOffset)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);
        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Null(packet.Payload);
        Assert.Contains(new ParseIssue(code, ParseIssueSeverity.Error, 12 + informationOffset), packet.Issues);
    }

    [Fact]
    public void DecodesAPlainPosition()
    {
        var report = Decode("!4903.50N/07201.75W-Test");

        Assert.Equal(49.058333, report.Position.Latitude, 6);
        Assert.Equal(-72.029167, report.Position.Longitude, 6);
        Assert.Equal(0, report.Position.Ambiguity);
        Assert.Equal(new AprsSymbol('/', '-'), report.Symbol);
        Assert.False(report.IsMessagingCapable);
        Assert.False(report.IsCompressed);
        Assert.Null(report.Timestamp);
        Assert.Null(report.Course);
        Assert.Equal("Test", report.Comment);
        Assert.Equal("Test"u8.ToArray(), report.CommentBytes.ToArray());
    }

    [Theory]
    [InlineData("!4903.50N/07201.75W-!W23!", 49.0583667, -72.0292167)]
    [InlineData("!4903.50S/07201.75E-!W23!", -49.0583667, 72.0292167)]
    [InlineData("!4903.50N/07201.75W-!wAb!", 49.0583919, -72.0292857)]
    [InlineData("!4903.50N/07201.75W-!W  !", 49.0583333, -72.0291667)]
    public void AppliesDaoPrecision(string information, double latitude, double longitude)
    {
        var report = Decode(information);

        Assert.Equal(latitude, report.Position.Latitude, 6);
        Assert.Equal(longitude, report.Position.Longitude, 6);
        Assert.Equal(char.ToUpperInvariant(information[^4]), char.ToUpperInvariant(report.Datum!.Value));
        Assert.Equal("", report.Comment);
        Assert.EndsWith("!", Encoding.ASCII.GetString(report.CommentBytes.Span), StringComparison.Ordinal);
    }

    [Fact]
    public void DaoIsRemovedFromTheMiddleOfAComment()
    {
        Assert.Equal("ab cd", Decode("!4903.50N/07201.75W-ab!W23! cd").Comment);
    }

    [Fact]
    public void DaoPrecisionIsIgnoredOnAnAmbiguousPosition()
    {
        var report = Decode("!4903.5 N/07201.75W-!W99!");

        Assert.Equal(49.058333, report.Position.Latitude, 6);
        Assert.Equal('W', report.Datum);
    }

    [Theory]
    [InlineData("!4903.50N/07201.75W-!X9x!")]
    [InlineData("!4903.50N/07201.75W-!w}a!")]
    [InlineData("!4903.50N/07201.75W-!!!!!")]
    public void TextThatIsNotADaoTokenStaysInTheComment(string information)
    {
        var report = Decode(information);

        Assert.Null(report.Datum);
        Assert.Equal(information[20..], report.Comment);
    }

    [Theory]
    [InlineData("!4903.50N/07201.75W-/A=001234", 1234.0)]
    [InlineData("!4903.50N/07201.75W-hi /A=-00012 there", -12.0)]
    [InlineData("!4903.50N/07201.75W-/A=12", null)]
    [InlineData("!4903.50N/07201.75W-/A=12x456", null)]
    public void ReadsAltitudeFromTheComment(string information, double? altitude)
    {
        Assert.Equal(altitude, Decode(information).AltitudeFeet);
    }

    [Fact]
    public void ReadsAltitudeFromACompressedPositionComment()
    {
        Assert.Equal(1234.0, Decode("=/5L!!<*e7>   /A=001234").AltitudeFeet);
    }

    [Fact]
    public void AcceptsLowerCaseHemispheresWithAWarning()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:!3907.62n/09447.19wh");

        var report = Assert.IsType<PositionReport>(packet.Payload);
        Assert.Equal(39.127, report.Position.Latitude, 3);
        Assert.Equal(-94.786500, report.Position.Longitude, 6);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Equal(
            [new ParseIssue(ParseIssueCode.LowercaseHemisphere, ParseIssueSeverity.Warning, 12 + 8)],
            packet.Issues
        );
    }

    [Fact]
    public void ALowerCaseLongitudeAloneIsFlaggedThere()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:!3907.62S/09447.19eh");

        Assert.Equal(-39.127, ((PositionReport)packet.Payload!).Position.Latitude, 3);
        Assert.Equal(12 + 18, Assert.Single(packet.Issues).Offset);
    }

    [Fact]
    public void ABrokenTimestampOnlyLosesTheTime()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:@122300#3950.70N/10505.14W&PHG8230");

        var report = Assert.IsType<PositionReport>(packet.Payload);
        Assert.Null(report.Timestamp);
        Assert.True(report.IsMessagingCapable);
        Assert.Equal(39.845, report.Position.Latitude, 3);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Equal([new ParseIssue(ParseIssueCode.InvalidTimestamp, ParseIssueSeverity.Warning, 13)], packet.Issues);
    }

    [Fact]
    public void ABrokenTimestampWithoutAPositionIsAnError()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:@1223xx#hello");

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Equal(new ParseIssue(ParseIssueCode.InvalidTimestamp, ParseIssueSeverity.Error, 13), packet.Issues[0]);
    }

    [Theory]
    [InlineData("!4903.50N/07201.75", ParseIssueCode.PositionTooShort, 1)]
    [InlineData("=/5L!!<*e7", ParseIssueCode.PositionTooShort, 1)]
    [InlineData("!9103.50N/07201.75W-", ParseIssueCode.InvalidLatitude, 1)]
    [InlineData("!4960.00N/07201.75W-", ParseIssueCode.InvalidLatitude, 1)]
    [InlineData("!4903,50N/07201.75W-", ParseIssueCode.InvalidLatitude, 1)]
    [InlineData("!4903.50X/07201.75W-", ParseIssueCode.InvalidLatitude, 1)]
    [InlineData("!4903.50N/18101.75W-", ParseIssueCode.InvalidLongitude, 10)]
    [InlineData("!4903.50N/07201.75Q-", ParseIssueCode.InvalidLongitude, 10)]
    [InlineData("!4903.50N/0720A.75W-", ParseIssueCode.InvalidLongitude, 10)]
    [InlineData("!4903.50N&07201.75W-", ParseIssueCode.InvalidSymbolTable, 9)]
    [InlineData("!4903.50N/07201.75W\u007F", ParseIssueCode.InvalidSymbolCode, 9)]
    [InlineData("=/5L !<*e7>7P[", ParseIssueCode.InvalidLatitude, 2)]
    [InlineData("=/5L!!<* 7>7P[", ParseIssueCode.InvalidLongitude, 6)]
    [InlineData("=}5L!!<*e7>7P[", ParseIssueCode.InvalidSymbolTable, 1)]
    public void RejectsMalformedPositions(string information, ParseIssueCode code, int offset)
    {
        Fail(information, code, offset);
    }

    [Theory]
    [InlineData("=/5L!!<*e7>7P[", '/')]
    [InlineData("=a5L!!<*e7>7P[", '0')]
    [InlineData("=j5L!!<*e7>7P[", '9')]
    [InlineData("=Z5L!!<*e7>7P[", 'Z')]
    public void ReadsTheCompressedSymbolTable(string information, char table)
    {
        Assert.Equal(table, Decode(information).Symbol.Table);
    }

    [Fact]
    public void ACompressedPositionWithoutCourseIgnoresTheTypeByte()
    {
        var report = Decode("=/5L!!<*e7> sTComment");

        Assert.Null(report.CompressionType);
        Assert.Null(report.Course);
        Assert.Equal("Comment", report.Comment);
        Assert.True(report.IsCompressed);
    }

    [Fact]
    public void ACompressedTypeByteOutOfRangeIsIgnored()
    {
        var report = Decode("=/5L!!<*e7>7P\u007F");

        Assert.Null(report.CompressionType);
        Assert.Null(report.Course);
    }

    [Fact]
    public void CommentsDecodeAsUtf8WhenTheyCan()
    {
        var utf8 = AprsPacket.Parse([.. "N0CALL>APRS:!4903.50N/07201.75W-"u8, .. "21.8 °C"u8]);
        var latin1 = AprsPacket.Parse([.. "N0CALL>APRS:!4903.50N/07201.75W-"u8, .. "21.8 "u8, 0xB0, (byte)'C']);

        Assert.Equal("21.8 °C", ((PositionReport)utf8.Payload!).Comment);
        Assert.Equal("21.8 °C", ((PositionReport)latin1.Payload!).Comment);
    }

    [Theory]
    [InlineData("=4903.50N/07201.75W-...", null, null)]
    [InlineData("=4903.50N/07201.75W-361/010", null, null)]
    [InlineData("=4903.50N/07201.75W-   /   ", null, null)]
    [InlineData("=4903.50N/07201.75W-090/0x0", null, null)]
    [InlineData("=4903.50N/07201.75W-PHG51", null, null)]
    [InlineData("=4903.50N/07201.75W-RNG00x0", null, null)]
    public void IncompleteDataExtensionsStayInTheComment(string information, int? course, double? speed)
    {
        var report = Decode(information);

        Assert.Equal(course, report.Course);
        Assert.Equal(speed, report.SpeedKnots);
        Assert.Null(report.PowerHeightGain);
        Assert.Null(report.RangeMiles);
        if (information.EndsWith("   /   ", StringComparison.Ordinal))
        {
            return;
        }

        Assert.Equal(information[20..], report.Comment);
    }

    [Fact]
    public void BlankCourseAndSpeedAreReadAsNotKnown()
    {
        var report = Decode("=4903.50N/07201.75W-   /   rest");

        Assert.Null(report.Course);
        Assert.Null(report.SpeedKnots);
        Assert.Equal("rest", report.Comment);
    }

    [Fact]
    public void ADfSymbolWithoutABearingReportKeepsTheRest()
    {
        var report = Decode("=4903.50N/07201.75W\\088/036 no bearing");

        Assert.Equal(88, report.Course);
        Assert.Null(report.DirectionFinding);
        Assert.Equal(" no bearing", report.Comment);
    }

    [Fact]
    public void TheNullPositionIsUnknown()
    {
        Assert.True(Decode("!0000.00N\\00000.00W.").IsPositionUnknown);
        Assert.False(Decode("!0000.00N/00000.00W.").IsPositionUnknown);
    }

    [Fact]
    public void ResolvesZuluTimestamps()
    {
        var received = new DateTimeOffset(2026, 10, 13, 1, 30, 0, TimeSpan.Zero);

        Assert.Equal(
            new DateTimeOffset(2026, 10, 12, 23, 45, 0, TimeSpan.Zero),
            Decode("/122345z4903.50N/07201.75W-").Timestamp!.Value.ToDateTimeOffset(received)
        );
        Assert.Equal(
            new DateTimeOffset(2026, 9, 30, 23, 45, 0, TimeSpan.Zero),
            Decode("/302345z4903.50N/07201.75W-").Timestamp!.Value.ToDateTimeOffset(received)
        );
        Assert.Equal(
            new DateTimeOffset(2026, 10, 13, 0, 10, 5, TimeSpan.Zero),
            Decode("/001005h4903.50N/07201.75W-").Timestamp!.Value.ToDateTimeOffset(received)
        );
        Assert.Equal(
            new DateTimeOffset(2026, 10, 12, 23, 10, 5, TimeSpan.Zero),
            Decode("/231005h4903.50N/07201.75W-").Timestamp!.Value.ToDateTimeOffset(received)
        );
    }

    [Fact]
    public void ADayMissingFromRecentMonthsResolvesToTheLastMonthThatHasIt()
    {
        // Received 2 March: the 31st is not in February, and 31 March is weeks ahead, so it is 31 January.
        var received = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero),
            Decode("/311200z4903.50N/07201.75W-").Timestamp!.Value.ToDateTimeOffset(received)
        );
    }

    [Fact]
    public void LocalTimestampsCannotBeResolved()
    {
        var timestamp = Decode("@092345/4903.50N/07201.75W-").Timestamp!.Value;

        Assert.True(timestamp.IsLocal);
        Assert.Null(timestamp.ToDateTimeOffset(DateTimeOffset.UnixEpoch));
        Assert.Equal("092345/", timestamp.ToString());
        Assert.Equal("234517h", Decode("/234517h4903.50N/07201.75W-").Timestamp!.Value.ToString());
    }

    [Theory]
    [InlineData("/322345z")]
    [InlineData("/002345z")]
    [InlineData("/092445z")]
    [InlineData("/092360z")]
    [InlineData("/240000h")]
    [InlineData("/236000h")]
    [InlineData("/235960h")]
    [InlineData("/092345x")]
    public void RejectsInvalidTimestamps(string timestamp)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + timestamp + "4903.50N/07201.75W-");

        Assert.Contains(packet.Issues, i => i.Code == ParseIssueCode.InvalidTimestamp);
        Assert.Null(((PositionReport)packet.Payload!).Timestamp);
    }

    [Fact]
    public void EveryPositionIssueCodeHasADescription()
    {
        Assert.EndsWith(".", ParseIssueCode.LowercaseHemisphere.Describe(), StringComparison.Ordinal);
        Assert.EndsWith(".", ParseIssueCode.InvalidSymbolCode.Describe(), StringComparison.Ordinal);
    }
}
