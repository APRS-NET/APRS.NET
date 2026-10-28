namespace AprsNet.Packets.Tests;

/// <summary><see cref="AddressSymbols"/>: the symbol from a packet's destination or source SSID, APRS101 pp. 92–93.</summary>
public sealed class AddressSymbolsTests
{
    [Theory]
    [InlineData("GPSMV", "/>")] // car
    [InlineData("SPCMV", "/>")]
    [InlineData("symmv", "/>")]
    [InlineData("GPSBB", "/!")] // police, the first primary code
    [InlineData("GPSJ4", "/~")] // the last
    [InlineData("GPSNV", "\\>")] // the alternate car
    [InlineData("GPSNV3", "3>")] // with an overlay
    [InlineData("GPSOBA", "A!")]
    [InlineData("GPSMV3", "/>")] // no overlay on the primary table
    [InlineData("GPSC12", "/,")] // Boy Scouts, by number
    [InlineData("GPSE12", "\\,")] // Girl Scouts
    [InlineData("GPSC01", "/!")]
    [InlineData("GPSE94", "\\~")]
    public void ADestinationNamesASymbol(string destination, string symbol)
    {
        Assert.True(AddressSymbols.TryFromDestination(destination, out var found));
        Assert.Equal(symbol, found.ToString());
    }

    [Theory]
    [InlineData("GPS")]
    [InlineData("APRS")]
    [InlineData("APZNET")]
    [InlineData("GPSZZ")] // not a code
    [InlineData("GPSC00")]
    [InlineData("GPSC95")]
    [InlineData("SPCC12")] // numbers only after GPS
    [InlineData("GPSMVXY")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherDestinationsNameNone(string? destination) =>
        Assert.False(AddressSymbols.TryFromDestination(destination, out _));

    [Theory]
    [InlineData("1", "/a")]
    [InlineData("7", "/'")]
    [InlineData("9", "/>")]
    [InlineData("10", "/<")]
    [InlineData("15", "/v")]
    public void AnSsidNamesASymbol(string ssid, string symbol)
    {
        Assert.True(AddressSymbols.TryFromSsid(ssid, out var found));
        Assert.Equal(symbol, found.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("16")]
    [InlineData("09")]
    [InlineData("DS")]
    public void OtherSsidsNameNone(string ssid) => Assert.False(AddressSymbols.TryFromSsid(ssid, out _));

    [Theory]
    [InlineData("N0CALL-9>GPSMV:$GPGGA,", "/>", AddressSymbolSource.Destination)]
    [InlineData("N0CALL-7>GPSNV:$GPGGA,", "\\>", AddressSymbolSource.Destination)] // destination first
    [InlineData("N0CALL-9>GPS:$GPGGA,", "/>", AddressSymbolSource.SourceSsid)]
    [InlineData("N0CALL-9>GPSMV-2:$GPGGA,", "/>", AddressSymbolSource.Destination)] // the destination's SSID aside
    public void ARawNmeaPacketTakesItsSymbolFromItsAddress(string header, string symbol, AddressSymbolSource from)
    {
        var report = Assert.IsType<NmeaReport>(AprsPacket.Parse(header + "120000,0.0,N,0.0,E,0,00,,,M,,M,,*").Payload);

        Assert.Equal(symbol, report.Symbol.ToString());
        Assert.Equal(from, report.SymbolSource);
    }

    [Fact]
    public void ARawNmeaPacketWithNeitherHasNoSymbol()
    {
        var report = Assert.IsType<NmeaReport>(AprsPacket.Parse("N0CALL>GPS:$GPGGA,120000,,,,,0,00,,,M,,M,,").Payload);

        Assert.Null(report.Symbol);
        Assert.Null(report.SymbolSource);
    }
}
