using AprsNet.Packets;

namespace AprsNet.Symbols.Tests;

public class AprsSymbolCatalogTests
{
    [Fact]
    public void HasEveryPrimaryAndAlternateCode()
    {
        var all = AprsSymbolCatalog.All;

        Assert.Equal(188, all.Count);
        Assert.Equal(94, all.Count(s => s.Symbol.IsPrimary));
        Assert.Equal(
            Enumerable.Range('!', 94).Select(c => (char)c),
            all.Where(s => s.Symbol.Table == '\\').Select(s => s.Symbol.Code)
        );
        Assert.Equal('/', all[0].Symbol.Table);
    }

    [Fact]
    public void EverySymbolHasANameAndADestinationCode()
    {
        Assert.All(
            AprsSymbolCatalog.All,
            s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Name));
                Assert.Equal(2, s.DestinationCode.Length);
                Assert.Equal(s.Name, s.ToString());
            }
        );
    }

    [Fact]
    public void DescribesASymbol()
    {
        var car = AprsSymbolCatalog.Get(new AprsSymbol('/', '>'));

        Assert.Equal("Car", car.Name);
        Assert.Equal(AprsSymbolStatus.Assigned, car.Status);
        Assert.Equal(AprsSymbolCategory.Vehicles, car.Category);
        Assert.Equal("MV", car.DestinationCode);
        Assert.False(car.IsOverlayFamily);
        Assert.Empty(car.OverlayNames);
    }

    [Fact]
    public void AnOverlayReturnsTheBaseAlternateSymbol()
    {
        var gateway = AprsSymbolCatalog.Get(new AprsSymbol('L', '&'));

        Assert.Equal(new AprsSymbol('\\', '&'), gateway.Symbol);
        Assert.True(gateway.IsOverlayFamily);
        Assert.Equal("Lora Igate", gateway.OverlayNames['L']);
    }

    [Theory]
    [InlineData('L', '&', "Lora Igate")]
    [InlineData('\\', '&', "Gateway (diamond)")]
    [InlineData('Q', '&', "Gateway (diamond)")]
    [InlineData('/', '-', "House (VHF)")]
    public void GetNamePrefersTheOverlayMeaning(char table, char code, string name)
    {
        Assert.Equal(name, AprsSymbolCatalog.GetName(new AprsSymbol(table, code)));
    }

    [Theory]
    [InlineData('/', 'D', AprsSymbolStatus.Unassigned)]
    [InlineData('/', '|', AprsSymbolStatus.Reserved)]
    [InlineData('\\', '*', AprsSymbolStatus.Legacy)]
    public void KnowsCodesWithoutACurrentMeaning(char table, char code, AprsSymbolStatus status)
    {
        Assert.Equal(status, AprsSymbolCatalog.Get(new AprsSymbol(table, code)).Status);
    }

    [Fact]
    public void RejectsTheDefaultSymbol()
    {
        Assert.Throws<ArgumentException>("symbol", () => AprsSymbolCatalog.Get(default));
    }
}
