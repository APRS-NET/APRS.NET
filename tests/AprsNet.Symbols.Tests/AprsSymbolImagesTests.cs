using System.Xml.Linq;
using AprsNet.Packets;

namespace AprsNet.Symbols.Tests;

public class AprsSymbolImagesTests
{
    private static readonly XNamespace _svg = "http://www.w3.org/2000/svg";

    public static TheoryData<AprsSymbolStyle> Styles() => [.. Enum.GetValues<AprsSymbolStyle>()];

    [Theory]
    [MemberData(nameof(Styles))]
    public void EverySymbolIsAValidScalableSvg(AprsSymbolStyle style)
    {
        foreach (var info in AprsSymbolCatalog.All)
        {
            AssertValid(AprsSymbolImages.GetSvg(info.Symbol, style), info.Symbol);
        }
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void EveryOverlayOnEveryAlternateSymbolIsValid(AprsSymbolStyle style)
    {
        var overlays = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        foreach (var info in AprsSymbolCatalog.All.Where(s => !s.Symbol.IsPrimary))
        {
            foreach (var overlay in overlays)
            {
                AssertValid(AprsSymbolImages.GetSvg(new AprsSymbol(overlay, info.Symbol.Code), style), info.Symbol);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void AnOverlayChangesTheImage(AprsSymbolStyle style)
    {
        Assert.NotEqual(
            AprsSymbolImages.GetSvg(new AprsSymbol('\\', '&'), style),
            AprsSymbolImages.GetSvg(new AprsSymbol('L', '&'), style)
        );
        Assert.NotEqual(
            AprsSymbolImages.GetSvg(new AprsSymbol('\\', '-'), style),
            AprsSymbolImages.GetSvg(new AprsSymbol('E', '-'), style)
        );
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void UnassignedAndReservedCodesShareOneImage(AprsSymbolStyle style)
    {
        var unassigned = AprsSymbolImages.GetSvg(new AprsSymbol('/', 'D'), style);

        Assert.Equal(unassigned, AprsSymbolImages.GetSvg(new AprsSymbol('\\', '~'), style));
        Assert.Equal(unassigned, AprsSymbolImages.GetSvg(new AprsSymbol('A', 'Z'), style));
        Assert.NotEqual(unassigned, AprsSymbolImages.GetSvg(new AprsSymbol('/', '>'), style));
    }

    [Fact]
    public void TheStylesDiffer()
    {
        var car = new AprsSymbol('/', '>');

        Assert.NotEqual(
            AprsSymbolImages.GetSvg(car, AprsSymbolStyle.Conventional),
            AprsSymbolImages.GetSvg(car, AprsSymbolStyle.Modern)
        );
        Assert.Equal(AprsSymbolImages.GetSvg(car), AprsSymbolImages.GetSvg(car, AprsSymbolStyle.Conventional));
    }

    [Fact]
    public void ImagesAreStable()
    {
        var symbol = new AprsSymbol('I', '#');

        Assert.Equal(
            AprsSymbolImages.GetSvg(symbol, AprsSymbolStyle.Modern),
            AprsSymbolImages.GetSvg(symbol, AprsSymbolStyle.Modern)
        );
    }

    [Fact]
    public void RejectsAnUnknownStyle()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            "style",
            () => AprsSymbolImages.GetSvg(new AprsSymbol('/', '>'), (AprsSymbolStyle)99)
        );
    }

    private static void AssertValid(string svg, AprsSymbol symbol)
    {
        var root = XDocument.Parse(svg).Root!;

        Assert.Equal(_svg + "svg", root.Name);
        Assert.Equal("0 0 48 48", root.Attribute("viewBox")?.Value);
        Assert.True(root.HasElements, $"{symbol} has no drawing");
        Assert.DoesNotContain("cut:", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
    }
}
