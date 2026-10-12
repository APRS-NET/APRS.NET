namespace AprsNet.Symbols;

/// <summary>One visual style for the whole set. Every style draws from the same catalogue.</summary>
internal interface ISymbolStyle
{
    /// <summary>
    /// The SVG for a symbol (key: table and code, such as <c>/&gt;</c>), with an overlay character drawn on it, or
    /// null when the style has no drawing for it.
    /// </summary>
    string? Draw(string key, AprsSymbolCategory category, char? overlay);

    /// <summary>The shared icon for codes with no assigned meaning.</summary>
    string Unassigned();
}
