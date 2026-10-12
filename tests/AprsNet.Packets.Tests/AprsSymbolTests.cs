namespace AprsNet.Packets.Tests;

public class AprsSymbolTests
{
    [Theory]
    [InlineData('/', '>', true, null, "/>")]
    [InlineData('\\', '#', false, null, "\\#")]
    [InlineData('E', '0', false, 'E', "E0")]
    [InlineData('1', '#', false, '1', "1#")]
    public void DescribesTableCodeAndOverlay(char table, char code, bool primary, char? overlay, string text)
    {
        var symbol = new AprsSymbol(table, code);

        Assert.Equal(table, symbol.Table);
        Assert.Equal(code, symbol.Code);
        Assert.Equal(primary, symbol.IsPrimary);
        Assert.Equal(overlay, symbol.Overlay);
        Assert.Equal(text, symbol.ToString());
    }

    [Fact]
    public void WithoutOverlayMovesToTheAlternateTable()
    {
        Assert.Equal(new AprsSymbol('\\', '&'), new AprsSymbol('L', '&').WithoutOverlay);
        Assert.Equal(new AprsSymbol('/', '&'), new AprsSymbol('/', '&').WithoutOverlay);
        Assert.Equal(new AprsSymbol('\\', '&'), new AprsSymbol('\\', '&').WithoutOverlay);
    }

    [Theory]
    [InlineData('a', '>')]
    [InlineData('&', '>')]
    [InlineData(' ', '>')]
    [InlineData('/', ' ')]
    [InlineData('/', '\u007F')]
    public void RejectsInvalidTablesAndCodes(char table, char code)
    {
        Assert.False(AprsSymbol.TryCreate(table, code, out var symbol));
        Assert.Equal(default, symbol);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AprsSymbol(table, code));
    }

    [Fact]
    public void TheDefaultSymbolHasNoOverlay()
    {
        Assert.Null(default(AprsSymbol).Overlay);
        Assert.Equal(default, default(AprsSymbol).WithoutOverlay);
    }

    [Fact]
    public void TryCreateAcceptsValidSymbols()
    {
        Assert.True(AprsSymbol.TryCreate('/', '_', out var symbol));
        Assert.Equal(new AprsSymbol('/', '_'), symbol);
    }
}
