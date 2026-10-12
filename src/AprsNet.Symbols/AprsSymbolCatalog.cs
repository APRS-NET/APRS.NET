using System.Collections.Frozen;
using System.Text.Json;
using AprsNet.Packets;

namespace AprsNet.Symbols;

/// <summary>
/// The meaning of every APRS symbol: the 94 primary and 94 alternate symbols, with the overlay meanings defined in
/// APRS 1.2. Loaded once from the catalogue embedded in this assembly.
/// </summary>
public static class AprsSymbolCatalog
{
    private static readonly Lazy<FrozenDictionary<AprsSymbol, AprsSymbolInfo>> _symbols = new(Load);

    /// <summary>Every primary and alternate symbol, primary table first, each in code order.</summary>
    public static IReadOnlyList<AprsSymbolInfo> All => _all.Value;

    private static readonly Lazy<AprsSymbolInfo[]> _all = new(() =>
        [.. _symbols.Value.Values.OrderBy(s => s.Symbol.IsPrimary ? 0 : 1).ThenBy(s => s.Symbol.Code)]
    );

    /// <summary>
    /// The base symbol's meaning. An overlay is ignored here: <c>E0</c> returns the information for <c>\0</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="symbol"/> is <see langword="default"/>.</exception>
    public static AprsSymbolInfo Get(AprsSymbol symbol) =>
        _symbols.Value.TryGetValue(symbol.WithoutOverlay, out var info)
            ? info
            : throw new ArgumentException("Not a symbol; create it with the AprsSymbol constructor.", nameof(symbol));

    /// <summary>
    /// The best name for a symbol as sent: the APRS 1.2 meaning of its overlay when one is defined (<c>L&amp;</c> is
    /// "Lora Igate"), otherwise the base symbol's name.
    /// </summary>
    public static string GetName(AprsSymbol symbol)
    {
        var info = Get(symbol);
        return symbol.Overlay is { } overlay && info.OverlayNames.TryGetValue(overlay, out var name) ? name : info.Name;
    }

    private static FrozenDictionary<AprsSymbol, AprsSymbolInfo> Load()
    {
        using var stream = typeof(AprsSymbolCatalog).Assembly.GetManifestResourceStream(
            "AprsNet.Symbols.catalogue.json"
        )!;
        using var document = JsonDocument.Parse(stream);
        var symbols = new Dictionary<AprsSymbol, AprsSymbolInfo>();
        foreach (var e in document.RootElement.GetProperty("symbols").EnumerateArray())
        {
            var symbol = new AprsSymbol(e.GetProperty("table").GetString()![0], e.GetProperty("code").GetString()![0]);
            var overlays = e.TryGetProperty("overlays", out var o)
                ? o.EnumerateArray()
                    .ToFrozenDictionary(
                        x => x.GetProperty("overlay").GetString()![0],
                        x => x.GetProperty("name").GetString()!
                    )
                : FrozenDictionary<char, string>.Empty;
            symbols[symbol] = new AprsSymbolInfo(
                symbol,
                e.GetProperty("name").GetString()!,
                Enum.Parse<AprsSymbolStatus>(e.GetProperty("status").GetString()!, ignoreCase: true),
                Enum.Parse<AprsSymbolCategory>(e.GetProperty("category").GetString()!, ignoreCase: true),
                e.GetProperty("gps").GetString()!,
                e.TryGetProperty("overlayFamily", out var family) && family.GetBoolean(),
                overlays
            );
        }

        return symbols.ToFrozenDictionary();
    }
}
