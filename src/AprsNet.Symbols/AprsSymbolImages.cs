using AprsNet.Packets;

namespace AprsNet.Symbols;

/// <summary>
/// Scalable images of APRS symbols as SVG markup, drawn by this library in a conventional or modern style. Every
/// image uses a 48×48 view box and scales cleanly to any size. Overlay characters are drawn with the library's own
/// outline font, so they look the same in every renderer.
/// </summary>
public static class AprsSymbolImages
{
    private static readonly ConventionalStyle _conventional = new();
    private static readonly ModernStyle _modern = new(_conventional);

    /// <summary>
    /// The SVG for a symbol, with its overlay character when it has one. Unassigned and reserved codes return the
    /// shared "not" sign (APRS 1.1).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="symbol"/> is <see langword="default"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> is not a defined style.</exception>
    public static string GetSvg(AprsSymbol symbol, AprsSymbolStyle style = AprsSymbolStyle.Conventional)
    {
        ISymbolStyle drawer = style switch
        {
            AprsSymbolStyle.Conventional => _conventional,
            AprsSymbolStyle.Modern => _modern,
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown symbol style."),
        };
        var info = AprsSymbolCatalog.Get(symbol);
        return drawer.Draw(info.Key, info.Category, symbol.Overlay) ?? drawer.Unassigned();
    }
}
