using AprsNet.Packets;

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
    public void TryParseAccepts(string text, string host, int port)
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
    public void TryParseRejects(string? text)
    {
        Assert.False(AprsIsEndpoint.TryParse(text, out var endpoint));
        Assert.Null(endpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ConstructorRejectsBlankHost(string host)
    {
        var ex = Assert.Throws<AprsValidationException>(() => new AprsIsEndpoint(host));
        Assert.Equal(AprsValidationErrorCode.ServerHostEmpty, ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void ConstructorRejectsBadPort(int port)
    {
        var ex = Assert.Throws<AprsValidationException>(() => new AprsIsEndpoint("h", port));
        Assert.Equal(AprsValidationErrorCode.ServerPortOutOfRange, ex.Code);
        Assert.Equal("port", ex.ParamName);
    }

    [Fact]
    public void PresetsUseTheFilterPort()
    {
        Assert.Equal(AprsIsPorts.UserDefinedFilter, AprsIsEndpoint.Rotate.Port);
        Assert.Equal("noam.aprs2.net:14580", AprsIsEndpoint.NorthAmerica.ToString());
        Assert.Equal("rotate.aprs2.net:10152", AprsIsEndpoint.Rotate.WithPort(AprsIsPorts.FullFeed).ToString());
    }

    [Fact]
    public void ToStringBracketsIpv6()
    {
        Assert.Equal("[::1]:14580", new AprsIsEndpoint("::1").ToString());
    }
}
