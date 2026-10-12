namespace AprsNet.AprsIsTcpClient.Tests;

public class AprsIsEndpointTests
{
    [Theory]
    [InlineData("rotate.aprs2.net", "rotate.aprs2.net", 14580)]
    [InlineData("noam.aprs2.net:10152", "noam.aprs2.net", 10152)]
    [InlineData("  localhost:1  ", "localhost", 1)]
    [InlineData("127.0.0.1:65535", "127.0.0.1", 65535)]
    [InlineData("[::1]:14580", "::1", 14580)]
    [InlineData("[::1]", "::1", 14580)]
    public void TryParse_accepts(string text, string host, int port)
    {
        Assert.True(AprsIsEndpoint.TryParse(text, out var endpoint));
        Assert.Equal(host, endpoint.Host);
        Assert.Equal(port, endpoint.Port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":14580")]
    [InlineData("host:")]
    [InlineData("host:0")]
    [InlineData("host:65536")]
    [InlineData("host:-1")]
    [InlineData("host:abc")]
    [InlineData("::1")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    public void TryParse_rejects(string? text)
    {
        Assert.False(AprsIsEndpoint.TryParse(text, out var endpoint));
        Assert.Null(endpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_rejects_blank_host(string host) =>
        Assert.ThrowsAny<ArgumentException>(() => new AprsIsEndpoint(host));

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Constructor_rejects_bad_port(int port) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AprsIsEndpoint("h", port));

    [Fact]
    public void Presets_use_the_filter_port()
    {
        Assert.Equal(AprsIsPorts.UserDefinedFilter, AprsIsEndpoint.Rotate.Port);
        Assert.Equal("noam.aprs2.net:14580", AprsIsEndpoint.NorthAmerica.ToString());
        Assert.Equal("rotate.aprs2.net:10152", AprsIsEndpoint.Rotate.WithPort(AprsIsPorts.FullFeed).ToString());
    }

    [Fact]
    public void ToString_brackets_ipv6() => Assert.Equal("[::1]:14580", new AprsIsEndpoint("::1").ToString());
}
