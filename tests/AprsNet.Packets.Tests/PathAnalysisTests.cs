namespace AprsNet.Packets.Tests;

public class PathAnalysisTests
{
    private static PathAnalysis Analyse(string header) => PathAnalysis.FromPacket(AprsPacket.Parse(header + ":>test"));

    private static string[] Names(IEnumerable<PathEntry> entries) => [.. entries.Select(e => e.ToString())];

    // Headers from the corpus (anonymized), one per q-construct it holds. qAX and qAo never occur in it, so those two
    // follow the q-construct documentation's descriptions instead.
    [Theory]
    [InlineData("0Q0AAJ-1>APOT30,WIDE2-2,qAR,0Q7AAA-6", "qAR", "0Q7AAA-6", true, false, true)]
    [InlineData("QA1AVO-8>APZWSX,qAr,QA1ACP-1", "qAr", "QA1ACP-1", true, false, true)]
    [InlineData("0Q1AF>APKVPA,WIDE1-1,WIDE2-1,qAO,0Q1AG", "qAO", "0Q1AG", true, false, true)]
    [InlineData("N0CALL>APRS,WIDE2-1,qAo,N1ABC", "qAo", "N1ABC", true, false, true)]
    [InlineData("0Q1AD-D>APCHP0,qAS,0Q1AD", "qAS", "0Q1AD", false, false, false)]
    [InlineData("0Q0AA>APRS,TCPIP*,qAC,T2UK", "qAC", "T2UK", false, true, false)]
    [InlineData("N0CALL>APRS,TCPXX*,qAX,T2TEST", "qAX", "T2TEST", false, true, false)]
    [InlineData("0Q2AA>APZDMR,QTH*,TCPIP*,qAU,T2DENMARK", "qAU", "T2DENMARK", false, true, false)]
    [InlineData("QA0AAZ-12>APNL51,TCPIP*,qAI,QA0AAZ-12,QA0ABJ-10,T2LAUSITZ", "qAI", "QA0AAZ-12", false, true, false)]
    public void ReadsEachQConstruct(
        string header,
        string qConstruct,
        string entryStation,
        bool gatedFromRadio,
        bool fromInternet,
        bool heardDirectly
    )
    {
        var path = Analyse(header);

        Assert.Equal(qConstruct, path.QConstruct);
        Assert.Equal(entryStation, path.EntryStation?.Value);
        Assert.Equal(gatedFromRadio, path.IsGatedFromRadio);
        Assert.Equal(fromInternet, path.IsFromInternet);
        Assert.Equal(heardDirectly, path.IsHeardDirectly);
    }

    [Fact]
    public void ListsTheDigipeatersThatRepeatedThePacket()
    {
        // From the corpus: a digipeater that inserted its callsign, then a WIDE1-1 used up by one that did not.
        var path = Analyse("0Q2AAH-9>P2TVU9,0Q4AAH-3*,WIDE1*,WIDE2-1,qAR,0Q2AAI-1");

        Assert.Equal(["0Q4AAH-3*", "WIDE1*"], Names(path.UsedHops));
        Assert.Equal(["0Q4AAH-3"], path.Digipeaters.Select(c => c.Value));
        Assert.Equal(1, path.RemainingHopCount);
        Assert.False(path.IsHeardDirectly);
        Assert.True(path.IsGatedFromRadio);
    }

    [Fact]
    public void EveryEntryUpToTheLastMarkedOneWasUsed()
    {
        // TNC2 marks only the last digipeater; the ones before it repeated the packet too.
        var path = Analyse("N0CALL>APRS,N1ABC-1,N2ABC-2*,WIDE2-1,qAR,N3ABC");

        Assert.Equal(["N1ABC-1", "N2ABC-2"], path.Digipeaters.Select(c => c.Value));
    }

    [Fact]
    public void AnIGatesOwnBeaconOverTheInternetIsNotFromRadio()
    {
        // From the corpus: qAR, but TCPIP says the IGate sent it over its internet connection.
        var path = Analyse("0Q1AD-12>APRSGW,TCPIP*,qAR,0Q1AD-12");

        Assert.False(path.IsGatedFromRadio);
        Assert.True(path.IsFromInternet);
        Assert.False(path.IsHeardDirectly);
    }

    [Fact]
    public void ATraceListsEveryServer()
    {
        var path = Analyse("QA0AAZ-12>APNL51,TCPIP*,qAI,QA0AAZ-12,QA0ABJ-10,T2LAUSITZ,T2HUB2,T2BC");

        Assert.Equal(["QA0AAZ-12", "QA0ABJ-10", "T2LAUSITZ", "T2HUB2", "T2BC"], Names(path.AprsIsPath));
    }

    [Theory]
    [InlineData("N0CALL>APRS,WIDE1-1,WIDE2-2", 3)]
    [InlineData("N0CALL>APRS,WIDE1*,WIDE2-2", 2)]
    [InlineData("N0CALL>APRS,N1ABC-1*,WIDE2-1", 1)]
    [InlineData("N0CALL>APRS,N1ABC-1,WIDE2*", 0)]
    [InlineData("N0CALL>APRS,TRACE3-3", 3)]
    [InlineData("N0CALL>APRS,NCA2-1", 1)] // a regional alias
    [InlineData("N0CALL>APRS,WIDE2", 0)]
    [InlineData("N0CALL>APRS,RELAY,N1ABC", 0)]
    [InlineData("N0CALL>APRS", 0)]
    public void CountsTheHopsStillAskedFor(string header, int hops) =>
        Assert.Equal(hops, Analyse(header).RemainingHopCount);

    [Fact]
    public void APacketFromATncHasNoQConstruct()
    {
        var direct = Analyse("N0CALL>APRS,WIDE1-1,WIDE2-1");
        var repeated = Analyse("N0CALL>APRS,N1ABC-1*,WIDE2-1");

        Assert.Null(direct.QConstruct);
        Assert.Null(direct.EntryStation);
        Assert.True(direct.IsHeardDirectly);
        Assert.False(direct.IsGatedFromRadio);
        Assert.False(direct.IsFromInternet);
        Assert.False(repeated.IsHeardDirectly);
    }

    [Theory]
    [InlineData("N0CALL>APRS,NOGATE", true)]
    [InlineData("N0CALL>APRS,WIDE1-1,rfonly", true)]
    [InlineData("N0CALL>APRS,WIDE1-1", false)]
    public void SeesRequestsNotToGate(string header, bool noGate) => Assert.Equal(noGate, Analyse(header).IsNoGate);

    [Fact]
    public void OddPathsDoNotThrow()
    {
        var bare = Analyse("N0CALL>APRS,qAR");
        Assert.Equal("qAR", bare.QConstruct);
        Assert.Null(bare.EntryStation);
        Assert.Empty(bare.AprsIsPath);

        // Only the first q-construct counts; the rest is APRS-IS path.
        var twice = Analyse("N0CALL>APRS,WIDE1-1,qAR,N1ABC,qAS,T2TEST");
        Assert.Equal("qAR", twice.QConstruct);
        Assert.Equal("N1ABC", twice.EntryStation?.Value);
        Assert.Equal(["N1ABC", "qAS", "T2TEST"], Names(twice.AprsIsPath));

        var empty = PathAnalysis.FromPacket(AprsPacket.Parse("not a packet"));
        Assert.Null(empty.QConstruct);
        Assert.Empty(empty.UsedHops);
        Assert.Empty(empty.Digipeaters);
        Assert.False(empty.IsHeardDirectly);

        Assert.Same(empty, PathAnalysis.FromPath(null));
        Assert.True(PathAnalysis.FromPath([]).IsHeardDirectly);
        Assert.NotNull(PathAnalysis.FromPacket(AprsPacket.Parse("N0CALL>APRS,,*,WIDE1-1*:>test")));
    }

    [Fact]
    public void FromPacketRefusesNull() => Assert.Throws<ArgumentNullException>(() => PathAnalysis.FromPacket(null!));
}
