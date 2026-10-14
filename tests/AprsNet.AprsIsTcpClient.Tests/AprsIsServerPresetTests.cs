namespace AprsNet.AprsIsTcpClient.Tests;

public class AprsIsServerPresetTests
{
    [Fact]
    public void ListsEveryEndpointPresetOnceInAFixedOrder()
    {
        Assert.Equal(
            [
                AprsIsEndpoint.Rotate,
                AprsIsEndpoint.NorthAmerica,
                AprsIsEndpoint.SouthAmerica,
                AprsIsEndpoint.Europe,
                AprsIsEndpoint.Asia,
                AprsIsEndpoint.Oceania,
            ],
            AprsIsServerPreset.All.Select(p => p.Endpoint)
        );
    }

    [Fact]
    public void EndpointsAreThePresetPropertiesThemselves()
    {
        Assert.Same(AprsIsEndpoint.Europe, AprsIsServerPreset.All[3].Endpoint);
    }

    [Fact]
    public void ListsEveryPublicEndpointPreset()
    {
        // A new preset property on AprsIsEndpoint must be added to the list too.
        var properties = typeof(AprsIsEndpoint)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(AprsIsEndpoint))
            .Select(p => (AprsIsEndpoint)p.GetValue(null)!);

        Assert.Equal(properties.ToHashSet(), AprsIsServerPreset.All.Select(p => p.Endpoint).ToHashSet());
    }

    [Fact]
    public void WorldwideComesFirst()
    {
        var first = AprsIsServerPreset.All[0];

        Assert.Equal("rotate", first.Key);
        Assert.Equal("Worldwide", first.Name);
        Assert.Same(AprsIsEndpoint.Rotate, first.Endpoint);
    }

    [Fact]
    public void KeysAndNamesAreStable()
    {
        // Apps store these keys, so they must never change.
        Assert.Equal(
            [
                ("rotate", "Worldwide"),
                ("noam", "North America"),
                ("soam", "South America"),
                ("euro", "Europe and Africa"),
                ("asia", "Asia"),
                ("aunz", "Oceania"),
            ],
            AprsIsServerPreset.All.Select(p => (p.Key, p.Name))
        );
    }

    [Fact]
    public void KeysAreUniqueAndMatchTheHostNames()
    {
        Assert.Equal(AprsIsServerPreset.All.Count, AprsIsServerPreset.All.Select(p => p.Key).Distinct().Count());
        Assert.All(AprsIsServerPreset.All, p => Assert.Equal($"{p.Key}.aprs2.net", p.Endpoint.Host));
    }

    [Fact]
    public void EveryPresetUsesTheFilterPort()
    {
        Assert.All(AprsIsServerPreset.All, p => Assert.Equal(AprsIsPorts.UserDefinedFilter, p.Endpoint.Port));
    }

    [Fact]
    public void ToStringIsTheRegionName()
    {
        Assert.Equal("Europe and Africa", AprsIsServerPreset.All[3].ToString());
    }

    [Theory]
    [InlineData("euro", "euro")]
    [InlineData("EURO", "euro")]
    [InlineData("  aunz ", "aunz")]
    [InlineData("rotate", "rotate")]
    public void FindsByKey(string text, string key)
    {
        Assert.True(AprsIsServerPreset.TryFind(text, out var preset));
        Assert.Equal(key, preset.Key);
    }

    [Theory]
    [InlineData("euro.aprs2.net", "euro")]
    [InlineData("euro.aprs2.net:14580", "euro")]
    [InlineData("EURO.APRS2.NET:14580", "euro")]
    [InlineData("noam.aprs2.net:10152", "noam")] // the port is ignored: the full feed is the same region
    [InlineData(" rotate.aprs2.net ", "rotate")]
    [InlineData("asia.aprs2.net.", "asia")] // fully qualified
    public void FindsByServer(string text, string key)
    {
        Assert.True(AprsIsServerPreset.TryFind(text, out var preset));
        Assert.Equal(key, preset.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("europe")]
    [InlineData("first.aprs.net")]
    [InlineData("euro.aprs2.net.example.com")]
    [InlineData("t2test.aprs2.net:14580")] // a single Tier 2 server, not a rotate
    [InlineData("euro.aprs2.net:")] // not a valid server
    public void FindsNothingForOtherText(string? text)
    {
        Assert.False(AprsIsServerPreset.TryFind(text, out var preset));
        Assert.Null(preset);
    }

    [Fact]
    public void FindsByEndpoint()
    {
        Assert.True(AprsIsServerPreset.TryFind(AprsIsEndpoint.Asia.WithPort(AprsIsPorts.FullFeed), out var preset));
        Assert.Equal("asia", preset.Key);
        Assert.True(AprsIsServerPreset.TryFind(new AprsIsEndpoint("SOAM.aprs2.net", 1234), out preset));
        Assert.Equal("soam", preset.Key);
    }

    [Fact]
    public void FindsNothingForOtherEndpoints()
    {
        Assert.False(AprsIsServerPreset.TryFind(new AprsIsEndpoint("localhost"), out var preset));
        Assert.Null(preset);
        Assert.False(AprsIsServerPreset.TryFind((AprsIsEndpoint?)null, out preset));
        Assert.Null(preset);
    }
}
