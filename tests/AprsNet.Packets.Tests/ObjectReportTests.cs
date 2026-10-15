namespace AprsNet.Packets.Tests;

public class ObjectReportTests
{
    private static T Decode<T>(string information, out AprsPacket packet)
        where T : PositionedPayload
    {
        packet = AprsPacket.Parse("N0CALL>APRS:" + information);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        return Assert.IsType<T>(packet.Payload);
    }

    [Fact]
    public void DecodesALiveObject()
    {
        var obj = Decode<ObjectReport>(";LEADER   *092345z4903.50N/07201.75W>088/036Lead car", out var packet);

        Assert.Equal(PacketType.Object, packet.Type);
        Assert.Equal("LEADER", obj.Name);
        Assert.True(obj.IsLive);
        Assert.Equal("092345z", obj.Timestamp.ToString());
        Assert.Equal(49.058333, obj.Position.Latitude, 6);
        Assert.Equal(new AprsSymbol('/', '>'), obj.Symbol);
        Assert.Equal(88, obj.Course);
        Assert.Equal("Lead car", obj.Comment);
        Assert.Empty(packet.Issues);
    }

    [Fact]
    public void ObjectNamesKeepInnerSpaces()
    {
        Assert.Equal("146.94 R", Decode<ObjectReport>(";146.94 R *111111z4903.50N/07201.75Wr", out _).Name);
    }

    [Fact]
    public void ReadsAKilledCompressedObject()
    {
        var obj = Decode<ObjectReport>(";LEADER   _092345z/5L!!<*e7>7P[", out _);

        Assert.False(obj.IsLive);
        Assert.True(obj.IsCompressed);
        Assert.Equal(88, obj.Course);
    }

    [Fact]
    public void ABrokenObjectTimestampOnlyLosesTheTime()
    {
        var obj = Decode<ObjectReport>(";LEADER   *0923xxz4903.50N/07201.75W>", out var packet);

        Assert.Null(obj.Timestamp);
        Assert.Equal([new ParseIssue(ParseIssueCode.InvalidTimestamp, ParseIssueSeverity.Warning, 23)], packet.Issues);
    }

    [Theory]
    [InlineData(";LEADER   *092345z")]
    [InlineData(";LEADER   *0923")]
    [InlineData(";LEADER   X092345z4903.50N/07201.75W>")]
    [InlineData(";SHORT*0923454903.50N/07201.75W>")] // unpadded, and no valid timestamp after the marker
    [InlineData(";SHORT*092345zno position here")]
    public void RejectsMalformedObjects(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Null(packet.Payload);
    }

    [Theory]
    [InlineData(";EL-N0CAL*092345z4903.50N/07201.75W>", "EL-N0CAL", true)] // SvxLink's 8-character names
    [InlineData(";SHORT_092345z4903.50N/07201.75W>", "SHORT", false)]
    [InlineData(";A*092345z4903.50N/07201.75W>", "A", true)]
    public void ReadsAnUnpaddedNameWithAWarning(string information, string name, bool isLive)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        var report = (ObjectReport)packet.Payload!;
        Assert.Equal((name, isLive), (report.Name, report.IsLive));
        Assert.Equal(AprsTimestamp.DayHourMinute(9, 23, 45), report.Timestamp);
        Assert.Equal(49.0583, report.Position.Latitude, 4);
        Assert.Equal(
            [new ParseIssue(ParseIssueCode.UnpaddedObjectName, ParseIssueSeverity.Warning, 13)],
            packet.Issues
        );
    }

    [Fact]
    public void PrefersTheLongestNameThatFits()
    {
        // A '*' inside the name is allowed; the marker is the one a timestamp and position follow.
        var packet = AprsPacket.Parse("N0CALL>APRS:;A*B*092345z4903.50N/07201.75W>");

        Assert.Equal("A*B", ((ObjectReport)packet.Payload!).Name);
    }

    [Fact]
    public void AnUnpaddedObjectRebuildsWithItsNamePadded()
    {
        var parsed = AprsPacket.Parse("N0CALL>APRS:;EL-N0CAL*092345z4903.50N/07201.75W>");

        var rebuilt = AprsPacket.Create(parsed.Source!, parsed.Destination!, parsed.Path!, parsed.Payload!);

        Assert.Equal(
            ";EL-N0CAL *092345z4903.50N/07201.75W>",
            System.Text.Encoding.Latin1.GetString(rebuilt.Information.Span)
        );
    }

    [Fact]
    public void AnObjectWithoutAPositionIsAnError()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:;LEADER   *092345zhello world, no position");

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Contains(packet.Issues, i => i.Severity == ParseIssueSeverity.Error);
    }

    [Theory]
    [InlineData(")AID #2!4903.50N/07201.75WA", "AID #2", true)]
    [InlineData(")AID #2_4903.50N/07201.75WA", "AID #2", false)]
    [InlineData(")ABC!4903.50N/07201.75WA", "ABC", true)]
    [InlineData(")ABCDEFGHI!4903.50N/07201.75WA", "ABCDEFGHI", true)]
    public void DecodesItems(string information, string name, bool live)
    {
        var item = Decode<ItemReport>(information, out var packet);

        Assert.Equal(PacketType.Item, packet.Type);
        Assert.Equal(name, item.Name);
        Assert.Equal(live, item.IsLive);
        Assert.Null(item.Timestamp);
        Assert.Equal(new AprsSymbol('/', 'A'), item.Symbol);
    }

    [Theory]
    [InlineData(")AB!4903.50N/07201.75WA")]
    [InlineData(")ABCDEFGHIJ!4903.50N/07201.75WA")]
    [InlineData(")ABCDEF")]
    public void RejectsItemsWithoutAValidName(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Equal([new ParseIssue(ParseIssueCode.InvalidItem, ParseIssueSeverity.Error, 13)], packet.Issues);
    }

    [Theory]
    [InlineData("l010/310", 0, 3, AreaShapeKind.Circle, false)]
    [InlineData("l110/310", 1, 3, AreaShapeKind.Line, false)]
    [InlineData("l210/310", 2, 3, AreaShapeKind.Ellipse, false)]
    [InlineData("l310/310", 3, 3, AreaShapeKind.Triangle, false)]
    [InlineData("l410/310", 4, 3, AreaShapeKind.Box, false)]
    [InlineData("l9101510", 9, 15, AreaShapeKind.Box, true)]
    [InlineData("l610/310", 6, 3, AreaShapeKind.Line, false)]
    [InlineData("l710/010", 7, 0, AreaShapeKind.Ellipse, true)]
    public void ReadsAreaShapes(string extension, int type, int color, AreaShapeKind kind, bool filled)
    {
        var obj = Decode<ObjectReport>($";SEARCH   *092345z4903.50N\\07201.75W{extension}", out _);

        var area = Assert.IsType<AreaShape>(obj.Area);
        Assert.Equal(type, area.Type);
        Assert.Equal(color, area.Color);
        Assert.Equal(kind, area.Kind);
        Assert.Equal(filled, area.IsFilled);
        Assert.Equal(100 / 1500.0, area.LatitudeOffsetDegrees);
        Assert.Equal(100 / 1500.0, area.LongitudeOffsetDegrees);
        Assert.Null(obj.Course);
    }

    [Theory]
    [InlineData("l1100310")]
    [InlineData("l9101610")]
    [InlineData("lx10/310")]
    [InlineData("l10/310")]
    public void AnInvalidAreaExtensionStaysInTheComment(string extension)
    {
        var obj = Decode<ObjectReport>($";SEARCH   *092345z4903.50N\\07201.75W{extension}", out _);

        Assert.Null(obj.Area);
    }

    [Fact]
    public void OnlyLinesReadACorridorWidth()
    {
        var line = Decode<ObjectReport>(";ROUTE    *092345z4903.50N\\07201.75Wl610/310{100}", out _);
        var box = Decode<ObjectReport>(";ZONE     *092345z4903.50N\\07201.75Wl410/310{100}", out _);
        var noWidth = Decode<ObjectReport>(";ROUTE    *092345z4903.50N\\07201.75Wl610/310{abc}", out _);

        Assert.Equal(100, line.Area!.CorridorMiles);
        Assert.Null(box.Area!.CorridorMiles);
        Assert.Null(noWidth.Area!.CorridorMiles);
    }

    [Fact]
    public void AnAreaExtensionOnlyCountsOnTheAreaSymbol()
    {
        var obj = Decode<ObjectReport>(";CAR      *092345z4903.50N/07201.75W>010/310", out _);

        Assert.Null(obj.Area);
        Assert.Equal(10, obj.Course);
    }

    [Theory]
    [InlineData(")I91 3N!4903.50N\\07201.75Wm{55}", "55")]
    [InlineData(")SIGN!4903.50N\\07201.75Wm Speed {SLO} ahead", "SLO")]
    [InlineData(")SIGN!4903.50N\\07201.75Wm{}", null)]
    [InlineData(")SIGN!4903.50N\\07201.75Wm{1234}", null)]
    [InlineData(")SIGN!4903.50N\\07201.75Wm{55", null)]
    [InlineData(")SIGN!4903.50N\\07201.75Wm no braces", null)]
    [InlineData(")SIGN!4903.50N/07201.75Wm{55}", null)]
    public void ReadsSignposts(string information, string? signpost)
    {
        Assert.Equal(signpost, Decode<ItemReport>(information, out _).Signpost);
    }

    [Fact]
    public void ObjectsAndItemsArePositionedPayloads()
    {
        PositionedPayload[] payloads =
        [
            Decode<PositionReport>("!4903.50N/07201.75W-", out _),
            Decode<ObjectReport>(";LEADER   *092345z4903.50N/07201.75W>", out _),
            Decode<ItemReport>(")AID #2!4903.50N/07201.75WA", out _),
        ];

        Assert.All(payloads, p => Assert.Equal(49.058333, p.Position.Latitude, 6));
    }

    [Fact]
    public void ObjectIssueCodesHaveDescriptions()
    {
        Assert.EndsWith(".", ParseIssueCode.InvalidObject.Describe(), StringComparison.Ordinal);
        Assert.EndsWith(".", ParseIssueCode.InvalidItem.Describe(), StringComparison.Ordinal);
    }
}
