namespace AprsNet.Packets.Tests;

public sealed class QueryReportTests
{
    [Theory]
    [InlineData("?APRS?", "APRS", QueryKind.PositionAndStatus)]
    [InlineData("?IGATE?", "IGATE", QueryKind.IGate)]
    [InlineData("?WX?", "WX", QueryKind.Weather)]
    [InlineData("?TELEM?", "TELEM", QueryKind.Unknown)]
    public void AGeneralQueryIsAQueryReport(string information, string type, QueryKind kind)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        Assert.Equal(PacketType.Query, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Empty(packet.Issues);
        var query = Assert.IsType<QueryReport>(packet.Payload);
        Assert.Equal(type, query.QueryType);
        Assert.Equal(kind, query.Kind);
        Assert.Null(query.Footprint);
        Assert.Null(query.Argument);
    }

    [Theory]
    [InlineData("?APRS? 34.02,-117.15,0200", 34.02, -117.15, 200)]
    [InlineData("?APRS?-33.9,151.2,0050", -33.9, 151.2, 50)]
    [InlineData("?WX? 49.058,-72.029,0010", 49.058, -72.029, 10)]
    public void AFootprintGivesTheArea(string information, double latitude, double longitude, int radius)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        Assert.Empty(packet.Issues);
        var footprint = Assert.IsType<QueryReport>(packet.Payload).Footprint!.Value;
        Assert.Equal(latitude, footprint.Center.Latitude, 6);
        Assert.Equal(longitude, footprint.Center.Longitude, 6);
        Assert.Equal(radius, footprint.RadiusMiles);
    }

    [Theory]
    [InlineData("?APRS? somewhere")]
    [InlineData("?APRS? 34.02,-117.15")]
    [InlineData("?APRS? 95,-117.15,0200")]
    [InlineData("?APRS? 34.02,-117.15,12345")]
    public void AnUnreadableFootprintIsAWarning(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        var issue = Assert.Single(packet.Issues);
        Assert.Equal(ParseIssueCode.InvalidQueryFootprint, issue.Code);
        Assert.Equal(ParseIssueSeverity.Warning, issue.Severity);
        Assert.Equal(packet.ToString().IndexOf('?', StringComparison.Ordinal) + 6, issue.Offset);
        var query = Assert.IsType<QueryReport>(packet.Payload);
        Assert.Null(query.Footprint);
        Assert.Equal(information[7..], query.Argument);
    }

    [Theory]
    [InlineData("?")]
    [InlineData("?APRS")]
    [InlineData("??")]
    [InlineData("?AP RS?")]
    public void ATextThatIsNotAQueryIsMalformed(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        Assert.Equal(PacketType.Query, packet.Type);
        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Equal(ParseIssueCode.InvalidQuery, Assert.Single(packet.Issues).Code);
        Assert.Null(packet.Payload);
    }

    [Theory]
    [InlineData("APRS", null, "?APRS?")]
    [InlineData("IGATE", null, "?IGATE?")]
    [InlineData("APRS", 200, "?APRS? 34.02,-117.15,0200")]
    public void AQueryIsBuilt(string type, int? radius, string information)
    {
        var query = new QueryReport
        {
            QueryType = type,
            Footprint = radius is { } miles ? new QueryFootprint(new AprsPosition(34.02, -117.15), miles) : null,
        };

        var packet = AprsPacket.Create(Callsign.Parse("N0CALL"), Callsign.Parse("APRS"), [], query);

        Assert.Equal("N0CALL>APRS:" + information, packet.ToString());
    }

    [Fact]
    public void ASouthernFootprintHasItsSign()
    {
        var query = new QueryReport
        {
            QueryType = "APRS",
            Footprint = new QueryFootprint(new AprsPosition(-33.9, 151.2), 50),
        };

        var packet = AprsPacket.Create(Callsign.Parse("N0CALL"), Callsign.Parse("APRS"), [], query);

        Assert.Equal("N0CALL>APRS:?APRS?-33.9,151.2,0050", packet.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("AP RS")]
    [InlineData("APRS?")]
    public void AQueryTypeMustBeLettersOrDigits(string type)
    {
        Assert.Throws<ArgumentException>(() =>
            AprsPacket.Create(
                Callsign.Parse("N0CALL"),
                Callsign.Parse("APRS"),
                [],
                new QueryReport { QueryType = type }
            )
        );
    }

    [Fact]
    public void TheFootprintHoldsWhatIsWithinItsRadius()
    {
        var footprint = new QueryFootprint(new AprsPosition(34.02, -117.15), 200);

        Assert.True(footprint.Contains(new AprsPosition(34.02, -117.15)));
        Assert.True(footprint.Contains(new AprsPosition(32.72, -117.16))); // San Diego, about 90 miles
        Assert.False(footprint.Contains(new AprsPosition(37.77, -122.42))); // San Francisco, about 370 miles
    }

    [Theory]
    [InlineData("?APRSP", QueryKind.Position)]
    [InlineData("?APRSS", QueryKind.Status)]
    [InlineData("?APRSD", QueryKind.HeardDirect)]
    [InlineData("?APRSH N0QBF", QueryKind.Heard)]
    [InlineData("?APRSM", QueryKind.Messages)]
    [InlineData("?APRSO", QueryKind.Objects)]
    [InlineData("?APRST", QueryKind.Trace)]
    [InlineData("?PING?", QueryKind.Trace)]
    [InlineData("?aprsp", QueryKind.Position)]
    [InlineData("?TELEM?", QueryKind.Unknown)]
    public void ADirectedQueryNamesWhatItAsks(string text, QueryKind kind)
    {
        var message = Assert.IsType<MessagePayload>(AprsPacket.Parse($"N0CALL>APRS::KH2Z     :{text}").Payload);

        Assert.Equal(kind, message.QueryKind);
    }

    [Fact]
    public void AMessageThatIsNotAQueryHasNoQueryKind()
    {
        var message = Assert.IsType<MessagePayload>(AprsPacket.Parse("N0CALL>APRS::KH2Z     :APRSP").Payload);

        Assert.Equal(QueryKind.Unknown, message.QueryKind);
    }
}
