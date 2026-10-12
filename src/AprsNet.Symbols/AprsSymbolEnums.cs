namespace AprsNet.Symbols;

/// <summary>A visual style for symbol images.</summary>
public enum AprsSymbolStyle
{
    /// <summary>Shapes and colours long-time APRS users recognise, with dark outlines.</summary>
    Conventional,

    /// <summary>A white glyph on a disc coloured by the symbol's category.</summary>
    Modern,
}

/// <summary>Whether a symbol code has a meaning.</summary>
public enum AprsSymbolStatus
{
    /// <summary>The symbol has a current meaning.</summary>
    Assigned,

    /// <summary>The meaning moved to an overlay, but older stations still send the symbol (for example snow, <c>\*</c>).</summary>
    Legacy,

    /// <summary>The code has no meaning yet; images show the shared "not" sign.</summary>
    Unassigned,

    /// <summary>The code is reserved (the TNC stream-switch characters) and should not be sent.</summary>
    Reserved,
}

/// <summary>A broad grouping of symbols, used for filtering and for the modern style's colours.</summary>
public enum AprsSymbolCategory
{
    /// <summary>Radio, network and computers.</summary>
    Radio,

    /// <summary>Road and rail vehicles.</summary>
    Vehicles,

    /// <summary>Aircraft, space and navigation aids.</summary>
    Aircraft,

    /// <summary>Boats and the water.</summary>
    Marine,

    /// <summary>Emergency and safety.</summary>
    Emergency,

    /// <summary>Weather.</summary>
    Weather,

    /// <summary>Places and services.</summary>
    Places,

    /// <summary>People and animals.</summary>
    People,

    /// <summary>Markers and shapes.</summary>
    Markers,
}
