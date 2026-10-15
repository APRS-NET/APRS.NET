using AprsNet.Packets;

namespace AprsNet.Filters.Tests;

public sealed class AprsFilterTests
{
    // Overland Park, KS, and a station about 10 km north-east of it.
    private const string Home = "N0CALL-9>APRS,WIDE1-1,WIDE2-1,qAR,N0IGT-10:!3858.80N/09440.20W>Mobile";
    private const string Near = "N0ABC>APRS,TCPIP*,qAC,T2KANSAS:=3902.50N/09435.00W-Home";
    private const string Far = "VK2ABC>APRS,qAR,VK2IG:!3352.20S/15112.60E-Sydney";

    private static AprsPacket P(string line) => AprsPacket.Parse(line);

    private static bool Matches(string filter, string line, AprsFilterContext? context = null) =>
        AprsFilter.Parse(filter).Matches(P(line), context);

    [Theory]
    [InlineData("r/38.98/-94.67/20", Home, true)]
    [InlineData("r/38.98/-94.67/20", Near, true)]
    [InlineData("r/38.98/-94.67/5", Near, false)]
    [InlineData("r/38.98/-94.67/20", Far, false)]
    [InlineData("r/-33.87/151.21/10", Far, true)]
    public void RangeMatchesPositionsWithinTheDistance(string filter, string line, bool expected) =>
        Assert.Equal(expected, Matches(filter, line));

    [Fact]
    public void RangeIgnoresPacketsWithoutAPosition()
    {
        Assert.False(Matches("r/38.98/-94.67/20000", "N0CALL>APRS,qAC,T2:>Just a status"));
        Assert.False(Matches("r/0/0/100", "N0CALL>APRS,qAC,T2:!0000.00N\\00000.00W.Unknown"));
    }

    [Theory]
    [InlineData("a/40/-96/38/-94", Home, true)]
    [InlineData("a/40/-96/38/-95", Home, false)]
    [InlineData("a/-30/150/-35/152", Far, true)]
    public void AreaMatchesPositionsInsideTheBox(string filter, string line, bool expected) =>
        Assert.Equal(expected, Matches(filter, line));

    [Theory]
    [InlineData("p/N0", Home, true)]
    [InlineData("p/vk/ZL", Far, true)]
    [InlineData("p/N1/K", Home, false)]
    [InlineData("b/N0CALL-9", Home, true)]
    [InlineData("b/n0call*", Home, true)]
    [InlineData("b/N0CALL", Home, false)]
    [InlineData("b/*-9", Home, true)]
    [InlineData("b/N0*L-?", Home, false)]
    [InlineData("u/APRS", Home, true)]
    [InlineData("u/AP*", Home, true)]
    [InlineData("u/APDW*", Home, false)]
    [InlineData("e/N0IGT-10", Home, true)]
    [InlineData("e/T2*", Near, true)]
    [InlineData("e/T2*", Home, false)]
    public void CallsignTermsMatchTheirField(string filter, string line, bool expected) =>
        Assert.Equal(expected, Matches(filter, line));

    [Theory]
    [InlineData("o/LEADER", true)]
    [InlineData("o/LEAD*", true)]
    [InlineData("o/OTHER", false)]
    public void ObjectTermMatchesNamesWithoutTheirPadding(string filter, bool expected) =>
        Assert.Equal(expected, Matches(filter, "N0CALL>APRS,qAC,T2:;LEADER   *092345z4903.50N/07201.75W>Object"));

    [Fact]
    public void ObjectTermMatchesItems() =>
        Assert.True(Matches("o/AID*", "N0CALL>APRS,qAC,T2:)AID #2!4903.50N/07201.75WA"));

    [Theory]
    [InlineData("g/N0CALL", true)]
    [InlineData("g/N0*", true)]
    [InlineData("g/N1CALL", false)]
    public void GroupTermMatchesMessageAddressees(string filter, bool expected) =>
        Assert.Equal(expected, Matches(filter, "N1ABC>APRS,qAC,T2::N0CALL   :Hello{01"));

    [Theory]
    [InlineData("d/WIDE1", "N0A>APRS,N0DIG-1,WIDE1*,WIDE2-1,qAR,N0IG:>s", true)]
    [InlineData("d/N0DIG-1", "N0A>APRS,N0DIG-1,WIDE1*,WIDE2-1,qAR,N0IG:>s", true)]
    [InlineData("d/WIDE2-1", "N0A>APRS,N0DIG-1,WIDE1*,WIDE2-1,qAR,N0IG:>s", false)]
    [InlineData("d/N0IG", "N0A>APRS,N0DIG-1,WIDE1*,WIDE2-1,qAR,N0IG:>s", false)]
    [InlineData("d/N0DIG*", "N0A>APRS,N0DIG-1,WIDE2-1,qAR,N0IG:>s", false)]
    [InlineData("d/RS0ISS", "N0A>APRS,RS0ISS*,qAR,N0IG:>s", true)]
    public void DigipeaterTermMatchesStationsThatRepeatedThePacket(string filter, string line, bool expected) =>
        Assert.Equal(expected, Matches(filter, line));

    [Theory]
    [InlineData("q/R", Home, true)]
    [InlineData("q/CU", Near, true)]
    [InlineData("q/r", Home, false)]
    [InlineData("q/C", Home, false)]
    public void QConstructTermMatchesTheLetterExactly(string filter, string line, bool expected) =>
        Assert.Equal(expected, Matches(filter, line));

    [Theory]
    [InlineData("N0CALL>APRS,qAC,T2:!3858.80N/09440.20W>Mobile", "p")]
    [InlineData("N0CALL-9>S32UVT,qAC,T2:`(_fn\"Oj/]Mic-E", "p")]
    [InlineData("N0CALL>APRS,qAC,T2:;LEADER   *092345z4903.50N/07201.75W>Object", "o")]
    [InlineData("N0CALL>APRS,qAC,T2:)AID #2!4903.50N/07201.75WA", "i")]
    [InlineData("N0CALL>APRS,qAC,T2::N1ABC    :Hello", "m")]
    [InlineData("N0CALL>APRS,qAC,T2::N1ABC    :?APRSP", "mq")]
    [InlineData("N0CALL>APRS,qAC,T2:?APRS?", "q")]
    [InlineData("N0CALL>APRS,qAC,T2:>Status", "s")]
    [InlineData("N0CALL>APRS,qAC,T2:T#005,199,000,255,073,123,01101001", "t")]
    [InlineData("N0CALL>APRS,qAC,T2::N0CALL   :PARM.Battery,Temp", "mt")]
    [InlineData("N0CALL>APRS,qAC,T2:{Q1qwerty", "u")]
    [InlineData("N0CALL>APRS,qAC,T2::NWS-WARN :092010z,THUNDER,ARC_ASHLEY", "mn")]
    [InlineData("N0CALL>APRS,qAC,T2::NWS_WARN :092010z,THUNDER,ARC_ASHLEY", "mn")]
    [InlineData("N0CALL>APRS,qAC,T2:_10090556c220s004g005t077r000p000P000h50b09900wRSW", "w")]
    [InlineData("N0CALL>APRS,qAC,T2:@092345z4903.50N/07201.75W_220/004g005t077r000p000P000h50b09900", "pw")]
    [InlineData("N0CALL>APRS,qAC,T2:$ULTW0031003702CE0069----000086A00001----011901CC00000005", "w")]
    public void TypeTermMatchesEachPacketType(string line, string types)
    {
        foreach (var letter in "poimqstunw")
        {
            Assert.True(
                types.Contains(letter) == Matches($"t/{letter}", line),
                $"t/{letter} should {(types.Contains(letter) ? "" : "not ")}match {line}"
            );
        }
    }

    [Fact]
    public void TypeTermWithACallMeasuresFromThatStation()
    {
        var context = new AprsFilterContext
        {
            LastPosition = call => call == "N0CALL-9" ? new AprsPosition(38.98, -94.67) : null,
        };
        Assert.True(Matches("t/p/N0CALL-9/20", Near, context));
        Assert.False(Matches("t/p/N0CALL-9/5", Near, context));
        Assert.False(Matches("t/p/N9NONE/20000", Near, context));
        Assert.False(Matches("t/p/N0CALL-9/20", Near));
        Assert.False(Matches("t/s/N0CALL-9/20", Near, context));
    }

    [Fact]
    public void MyRangeAndFriendRangeNeedAContext()
    {
        var context = new AprsFilterContext
        {
            MyPosition = new AprsPosition(-33.87, 151.21),
            LastPosition = call => call == "N0CALL-9" ? new AprsPosition(38.98, -94.67) : null,
        };
        Assert.True(Matches("m/10", Far, context));
        Assert.False(Matches("m/10", Near, context));
        Assert.False(Matches("m/10", Far));
        Assert.True(Matches("f/N0CALL-9/20", Near, context));
        Assert.False(Matches("f/N0CALL-9/20", Far, context));
        Assert.False(Matches("f/N0CALL-9/20", Near));
    }

    [Theory]
    [InlineData("s/>", Home, true)]
    [InlineData("s/-", Home, false)]
    [InlineData("s/->", Near, true)]
    [InlineData("s//#", "N0A>APRS,qAC,T2:!3858.80N\\09440.20W#Digi", true)]
    [InlineData("s//#", "N0A>APRS,qAC,T2:!3858.80NS09440.20W#Digi", true)]
    [InlineData("s//#/S", "N0A>APRS,qAC,T2:!3858.80NS09440.20W#Digi", true)]
    [InlineData("s//#/T", "N0A>APRS,qAC,T2:!3858.80NS09440.20W#Digi", false)]
    [InlineData("s//#/S", "N0A>APRS,qAC,T2:!3858.80N\\09440.20W#Digi", false)]
    [InlineData("s/#", "N0A>APRS,qAC,T2:!3858.80N\\09440.20W#Digi", false)]
    public void SymbolTermMatchesTablesAndOverlays(string filter, string line, bool expected) =>
        Assert.Equal(expected, Matches(filter, line));

    [Theory]
    [InlineData("b/N0CALL-9 -p/N0", false)]
    [InlineData("b/N0CALL-9 -b/N1*", true)]
    [InlineData("r/38.98/-94.67/20 -t/p", false)]
    [InlineData("-p/N1", false)]
    [InlineData("p/N1 p/N0", true)]
    public void ExclusionsWinAndAreNotEnoughOnTheirOwn(string filter, bool expected) =>
        Assert.Equal(expected, Matches(filter, Home));

    [Fact]
    public void AnEmptyFilterMatchesNothing()
    {
        Assert.True(AprsFilter.Parse("  ").IsEmpty);
        Assert.False(AprsFilter.Parse("").Matches(P(Home)));
        Assert.False(AprsFilter.Empty.Matches(P(Home)));
        Assert.False(AprsFilter.Parse("p/N0").IsEmpty);
    }

    [Fact]
    public void HeaderTermsWorkOnPacketsWithoutAPayload()
    {
        var broken = P("N0CALL>APRS,qAR,N0IG:!9999.99N/09440.20W>bad");
        Assert.Equal(ParseOutcome.Malformed, broken.Outcome);
        Assert.True(AprsFilter.Parse("b/N0CALL q/R e/N0IG").Matches(broken));
        Assert.False(AprsFilter.Parse("r/38.98/-94.67/20000 t/poimqstunw").Matches(broken));
    }

    [Fact]
    public void ToStringNormalizesSpacing() =>
        Assert.Equal("r/1/2/3 -b/N0*", AprsFilter.Parse("  r/1/2/3 \t -b/N0*  ").ToString());

    [Theory]
    [InlineData("x/1", "'x' is not a filter type.")]
    [InlineData("r", "it has no '/' arguments.")]
    [InlineData("b/N0//N1", "it has an empty argument.")]
    [InlineData("r/1/2", "a range is r/lat/lon/km.")]
    [InlineData("r/91/2/3", "'91' is not a latitude.")]
    [InlineData("r/1/-181/3", "'-181' is not a longitude.")]
    [InlineData("r/1/2/-3", "'-3' is not a distance in km.")]
    [InlineData("r/1/2/NaN", "'NaN' is not a distance in km.")]
    [InlineData("a/1/2/3", "an area is a/latN/lonW/latS/lonE.")]
    [InlineData("a/38/-96/40/-94", "the north-west corner must be north and west of the south-east one.")]
    [InlineData("a/x/-96/40/-94", "its latitudes must be numbers from -90 to 90.")]
    [InlineData("a/40/-96/38/200", "its longitudes must be numbers from -180 to 180.")]
    [InlineData("t/px", "'x' is not a packet type; use the letters poimqstunw.")]
    [InlineData("t/p/N0CALL", "a type filter is t/types or t/types/call/km.")]
    [InlineData("t/p/N0CALL/far", "'far' is not a distance in km.")]
    [InlineData("s/a/b/c/d", "a symbol filter is s/pri, s/pri/alt or s/pri/alt/over, with at least one symbol.")]
    [InlineData("s//", "a symbol filter is s/pri, s/pri/alt or s/pri/alt/over, with at least one symbol.")]
    [InlineData("q/C/I", "only q/con is supported, without the /I option.")]
    [InlineData("m/1/2", "a my-range filter is m/km.")]
    [InlineData("m/x", "'x' is not a distance in km.")]
    [InlineData("f/N0CALL", "a friend-range filter is f/call/km.")]
    [InlineData("f/N0CALL/x", "'x' is not a distance in km.")]
    public void InvalidTermsSayWhy(string term, string reason)
    {
        Assert.False(AprsFilter.TryParse($"p/N0 -{term}", out var filter, out var error));
        Assert.Null(filter);
        Assert.Equal($"'-{term}' is not a valid filter: {reason}", error);
        Assert.Equal(error, Assert.Throws<FormatException>(() => AprsFilter.Parse($"p/N0 -{term}")).Message);
    }

    [Fact]
    public void NullIsRefused()
    {
        Assert.False(AprsFilter.TryParse(null, out _, out var error));
        Assert.Equal("A filter is required.", error);
        Assert.Throws<ArgumentNullException>(() => AprsFilter.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => AprsFilter.Empty.Matches(null!));
    }

    [Theory]
    [InlineData("*", "", true)]
    [InlineData("N0*", "N0CALL", true)]
    [InlineData("*CALL", "N0CALL", true)]
    [InlineData("N*L*9", "N0CALL-9", true)]
    [InlineData("N0**", "N0", true)]
    [InlineData("N0*X", "N0CALL", false)]
    [InlineData("N0", "N0CALL", false)]
    [InlineData("N0CALLS", "N0CALL", false)]
    public void GlobMatchesWholeValues(string pattern, string value, bool expected) =>
        Assert.Equal(expected, FilterTerm.Glob(pattern, value));
}
