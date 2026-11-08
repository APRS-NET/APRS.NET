namespace AprsNet.Gateway.Tests;

public sealed class ClientFilterTests
{
    private static string Terms(int count, Func<int, string> term) =>
        string.Join(' ', Enumerable.Range(0, count).Select(term));

    [Fact]
    public void RefusesMoreThanFiftyTerms()
    {
        Assert.False(ClientFilter.TryParse(Terms(51, i => $"p/N{i}"), out var filter, out var error));
        Assert.Null(filter);
        Assert.Equal("This server accepts at most 50 terms in a filter; this filter has 51.", error);
    }

    [Fact]
    public void CountsExcludingTermsTowardsTheLimit()
    {
        var text = Terms(25, i => $"p/N{i}") + " " + Terms(26, i => $"-p/K{i}");
        Assert.False(ClientFilter.TryParse(text, out _, out var error));
        Assert.Contains("this filter has 51", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesMoreRangeOrFriendTermsThanAprsIsAccepts()
    {
        Assert.False(ClientFilter.TryParse(Terms(10, i => $"f/N{i}CALL/1"), out _, out var friends));
        Assert.Equal("APRS-IS servers accept at most 9 friend (f/) terms; this filter has 10.", friends);
        Assert.False(ClientFilter.TryParse(Terms(10, i => $"r/{i}/0/1"), out _, out var ranges));
        Assert.Equal("APRS-IS servers accept at most 9 range (r/) terms; this filter has 10.", ranges);
    }

    [Fact]
    public void RefusesMorePatternCharactersThanTheLimitInOneTerm()
    {
        var text = "b/" + string.Join('/', Enumerable.Range(0, 103).Select(i => $"N{i:D3}*"));
        Assert.False(ClientFilter.TryParse(text, out _, out var error));
        Assert.Equal(
            "This server accepts at most 512 characters of callsign and name patterns in a filter; this filter has 515.",
            error
        );
    }

    [Fact]
    public void RefusesMorePatternCharactersThanTheLimitAcrossTerms()
    {
        var text = Terms(10, i => "o/" + new string((char)('A' + i), 52));
        Assert.False(ClientFilter.TryParse(text, out _, out var error));
        Assert.Contains("this filter has 520", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsATermThatDoesNotParseAsBefore()
    {
        Assert.False(ClientFilter.TryParse("r/91/0/10", out _, out var error));
        Assert.Equal("'r/91/0/10' is not a valid filter: '91' is not a latitude.", error);
    }

    [Fact]
    public void AcceptsARangeAndABuddyList()
    {
        var buddies = string.Join('/', Enumerable.Range(0, 50).Select(i => $"N{i}ABC-9"));
        Assert.True(ClientFilter.TryParse($"r/38.98/-94.67/50 m/25 t/m -b/N0CALL* b/{buddies}", out var filter, out _));
        Assert.Equal($"r/38.98/-94.67/50 m/25 t/m -b/N0CALL* b/{buddies}", filter.ToString());
    }

    [Fact]
    public void AcceptsFiftyTerms() => Assert.True(ClientFilter.TryParse(Terms(50, i => $"p/N{i}"), out _, out _));
}
