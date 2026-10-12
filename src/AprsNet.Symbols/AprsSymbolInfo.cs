using AprsNet.Packets;

namespace AprsNet.Symbols;

/// <summary>
/// What a base symbol means, from APRS101 Appendix 2 and the APRS 1.1 and 1.2 symbol lists. Get one from
/// <see cref="AprsSymbolCatalog"/>.
/// </summary>
public sealed class AprsSymbolInfo
{
    internal AprsSymbolInfo(
        AprsSymbol symbol,
        string name,
        AprsSymbolStatus status,
        AprsSymbolCategory category,
        string destinationCode,
        bool isOverlayFamily,
        IReadOnlyDictionary<char, string> overlayNames
    )
    {
        Symbol = symbol;
        Name = name;
        Status = status;
        Category = category;
        DestinationCode = destinationCode;
        IsOverlayFamily = isOverlayFamily;
        OverlayNames = overlayNames;
    }

    /// <summary>The symbol, on table <c>/</c> or <c>\</c>.</summary>
    public AprsSymbol Symbol { get; }

    /// <summary>The symbol's meaning in plain words, for example <c>Car</c> or <c>Digipeater (green star)</c>.</summary>
    public string Name { get; }

    /// <summary>Whether the code has a meaning.</summary>
    public AprsSymbolStatus Status { get; }

    /// <summary>The broad grouping the symbol belongs to.</summary>
    public AprsSymbolCategory Category { get; }

    /// <summary>
    /// The two characters that select this symbol in a <c>GPSxyz</c>, <c>SPCxyz</c> or <c>SYMxyz</c> destination,
    /// used by trackers that cannot put a symbol in the information field (APRS 1.1).
    /// </summary>
    public string DestinationCode { get; }

    /// <summary>
    /// Whether this is one of the original alternate symbols designed to carry an overlay (circle, box, digipeater,
    /// gateway, car, truck, van and others). Since APRS 1.2 any alternate symbol may carry one.
    /// </summary>
    public bool IsOverlayFamily { get; }

    /// <summary>Meanings defined for overlay characters on this alternate symbol, such as <c>I</c> on <c>\&amp;</c>.</summary>
    public IReadOnlyDictionary<char, string> OverlayNames { get; }

    internal string Key => Symbol.ToString();

    /// <summary>Returns <see cref="Name"/>.</summary>
    public override string ToString() => Name;
}
