namespace AprsNet.Packets.Tests;

public class CallsignTests
{
    [Theory]
    [InlineData("N0CALL", "N0CALL", "", true)]
    [InlineData("N0CALL-0", "N0CALL", "0", true)]
    [InlineData("N0CALL-9", "N0CALL", "9", true)]
    [InlineData("N0CALL-15", "N0CALL", "15", true)]
    [InlineData("K1A", "K1A", "", true)]
    [InlineData("N0CALL-16", "N0CALL", "16", false)]
    [InlineData("N0CALL-09", "N0CALL", "09", false)]
    [InlineData("N0CALL-DS", "N0CALL", "DS", false)]
    [InlineData("DEMO-002", "DEMO", "002", false)]
    [InlineData("N0CALL-", "N0CALL", "", false)]
    [InlineData("EL-N0CALL", "EL", "N0CALL", false)]
    [InlineData("n0call", "n0call", "", false)]
    [InlineData("T2PERTH", "T2PERTH", "", false)]
    [InlineData("qAR", "qAR", "", false)]
    public void SplitsBaseAndSsidAndChecksAx25(string value, string @base, string ssid, bool ax25)
    {
        var callsign = AprsPacket.Parse($"{value}>APRS:>status").Source!;

        Assert.Equal(value, callsign.Value);
        Assert.Equal(value, callsign.ToString());
        Assert.Equal(@base, callsign.Base);
        Assert.Equal(ssid, callsign.Ssid);
        Assert.Equal(ax25, callsign.IsAx25);
    }

    [Fact]
    public void EqualityIsByValueAndCaseSensitive()
    {
        var path = AprsPacket.Parse("N0CALL>APRS,qAR,QAR,qAR:>status").Path!;

        Assert.Equal(path[0].Station, path[2].Station);
        Assert.NotEqual(path[0].Station, path[1].Station);
    }
}
