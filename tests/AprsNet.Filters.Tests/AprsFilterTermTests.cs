using System.Globalization;
using AprsNet.Packets;

namespace AprsNet.Filters.Tests;

public class AprsFilterTermTests
{
    public static TheoryData<string, string> Golden() =>
        new()
        {
            { "r/38.9812/-94.6723/50", AprsFilterTerm.Range(new AprsPosition(38.98123, -94.67234), 50).ToString() },
            { "r/0/0/0.5", AprsFilterTerm.Range(new AprsPosition(-0.00001, 0), 0.5).ToString() },
            { "m/25", AprsFilterTerm.MyRange(25).ToString() },
            { "f/N0CALL-9/12.5", AprsFilterTerm.FriendRange("N0CALL-9", 12.54).ToString() },
            { "a/40/-100/35.5/-95", AprsFilterTerm.Area(40, -100, 35.5, -95).ToString() },
            { "p/N0/W1", AprsFilterTerm.Prefix("N0", "W1").ToString() },
            { "b/N0CALL*/N1ABC-7", AprsFilterTerm.Buddies("N0CALL*", "N1ABC-7").ToString() },
            { "o/LEADER/ISS*", AprsFilterTerm.Objects("LEADER", "ISS*").ToString() },
            { "t/poimqstunw", AprsFilterTerm.Types((AprsFilterTypes)0x3FF).ToString() },
            { "t/pw", AprsFilterTerm.Types(AprsFilterTypes.Weather | AprsFilterTypes.Positions).ToString() },
            { "t/m/N0CALL/100", AprsFilterTerm.Types(AprsFilterTypes.Messages, "N0CALL", 100).ToString() },
            { "s/->", AprsFilterTerm.Symbols("->").ToString() },
            { "s//#", AprsFilterTerm.Symbols("", "#").ToString() },
            { "s/k/#/RS", AprsFilterTerm.Symbols("k", "#", "RS").ToString() },
            { "d/WIDE*/N0DIGI", AprsFilterTerm.Digipeaters("WIDE*", "N0DIGI").ToString() },
            { "e/T2*", AprsFilterTerm.EntryStations("T2*").ToString() },
            { "g/BLN*/N0CALL", AprsFilterTerm.MessagesTo("BLN*", "N0CALL").ToString() },
            { "u/APDW*", AprsFilterTerm.Destinations("APDW*").ToString() },
            { "q/RO", AprsFilterTerm.QConstructs("RO").ToString() },
            { "-b/N0CALL*", AprsFilterTerm.Buddies("N0CALL*").Exclude().ToString() },
        };

    [Theory]
    [MemberData(nameof(Golden))]
    public void WritesTheServerSyntax(string expected, string actual) => Assert.Equal(expected, actual);

    [Theory]
    [MemberData(nameof(Golden))]
    public void EveryTermParsesBackToTheSameText(string expected, string _) =>
        Assert.Equal(expected, AprsFilter.Parse(expected).ToString());

    [Fact]
    public void IgnoresTheCurrentCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            // German writes 38,98 and would split the term at the comma; the filter must not change.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal("r/38.98/-94.67/12.5", AprsFilterTerm.Range(new AprsPosition(38.98, -94.67), 12.5).ToString());
            Assert.Equal("a/40.5/-100.25/35.5/-95.75", AprsFilterTerm.Area(40.5, -100.25, 35.5, -95.75).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void BuildsAFilterThatEvaluatesLikeTheParsedOne()
    {
        var built = AprsFilter.Create(
            AprsFilterTerm.Range(new AprsPosition(49.05, -72.03), 50),
            AprsFilterTerm.Buddies("N1ABC*"),
            AprsFilterTerm.Types(AprsFilterTypes.Weather).Exclude()
        );
        var parsed = AprsFilter.Parse(built.ToString());

        Assert.Equal("r/49.05/-72.03/50 b/N1ABC* -t/w", built.ToString());
        foreach (
            var line in new[]
            {
                "N0CALL>APRS:!4903.50N/07201.75W-near",
                "N0CALL>APRS:!3903.50N/07201.75W-far",
                "N1ABC-7>APRS:>far but a buddy",
                "N0CALL>APRS:!4903.50N/07201.75W_090/005g010t050",
            }
        )
        {
            var packet = AprsPacket.Parse(line);
            Assert.Equal(parsed.Matches(packet), built.Matches(packet));
        }

        Assert.True(built.Matches(AprsPacket.Parse("N0CALL>APRS:!4903.50N/07201.75W-near")));
        Assert.True(built.Matches(AprsPacket.Parse("N1ABC-7>APRS:>far but a buddy")));
        Assert.False(built.Matches(AprsPacket.Parse("N0CALL>APRS:!3903.50N/07201.75W-far")));
        Assert.False(built.Matches(AprsPacket.Parse("N0CALL>APRS:!4903.50N/07201.75W_090/005g010t050")));
    }

    [Fact]
    public void ExclusionsCombine()
    {
        var term = AprsFilterTerm.Prefix("N0");

        Assert.False(term.IsExclusion);
        Assert.True(term.Exclude().IsExclusion);
        Assert.Equal("-p/N0", term.Exclude().Exclude().ToString());
        Assert.Equal(
            "m/10 -p/N0 -t/s",
            AprsFilter
                .Create(
                    AprsFilterTerm.MyRange(10),
                    term.Exclude(),
                    AprsFilterTerm.Types(AprsFilterTypes.StatusReports).Exclude()
                )
                .ToString()
        );
        Assert.Equal("", AprsFilter.Create().ToString());
        Assert.True(AprsFilter.Create().IsEmpty);
    }

    [Fact]
    public void ReportsTheServerLimits()
    {
        var ranges = Enumerable.Range(0, 10).Select(i => AprsFilterTerm.Range(new AprsPosition(i, i), 10)).ToList();
        var friends = Enumerable.Range(0, 10).Select(i => AprsFilterTerm.FriendRange($"N{i}CALL", 10)).ToList();

        Assert.Null(AprsFilter.Create(ranges.Take(9).Concat(friends.Take(9))).CheckServerLimits());
        Assert.Contains("range (r/)", AprsFilter.Create(ranges).CheckServerLimits(), StringComparison.Ordinal);
        Assert.Contains("friend (f/)", AprsFilter.Create(friends).CheckServerLimits(), StringComparison.Ordinal);
        Assert.Contains(
            "this filter has 10",
            AprsFilter.Parse(string.Join(' ', ranges.Take(9).Append(ranges[9].Exclude()))).CheckServerLimits(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void AreaTakesBounds()
    {
        var bounds = new AprsPosition(49.05, -72.03, 4).GetAmbiguityBounds();

        Assert.Equal("a/50.05/-73.03/49.05/-72.03", AprsFilterTerm.Area(bounds).ToString());
    }

    [Fact]
    public void RefusesWhatCannotBeWritten()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsFilterTerm.MyRange(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsFilterTerm.MyRange(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsFilterTerm.MyRange(20_039));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsFilterTerm.Range(new AprsPosition(91, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsFilterTerm.Area(40, -181, 35, -95));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.Area(35, -100, 40, -95));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.Area(40, 170, 35, -170));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.Buddies());
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.Buddies("N0 CALL"));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.Objects("A/B"));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.Prefix("N0*"));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.FriendRange("N0*", 1));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.Buddies(""));
        Assert.Throws<ArgumentNullException>(() => AprsFilterTerm.Buddies(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsFilterTerm.Types(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsFilterTerm.Types((AprsFilterTypes)(1 << 10)));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.Symbols(""));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.Symbols("a b"));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.QConstructs(""));
        Assert.Throws<ArgumentException>(() => AprsFilterTerm.QConstructs("R1"));
        Assert.Throws<ArgumentNullException>(() => AprsFilter.Create(AprsFilterTerm.MyRange(1), null!));
    }
}
