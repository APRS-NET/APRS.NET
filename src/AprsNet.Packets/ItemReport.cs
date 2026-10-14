namespace AprsNet.Packets;

/// <summary>
/// An item (<c>)</c>, APRS101 ch. 11): like an object but for things that do not change over time, with a 3–9
/// character name and no timestamp. It is live or killed.
/// </summary>
public sealed class ItemReport : PositionedPayload
{
    internal ItemReport() { }

    internal ItemReport(PositionFields fields)
        : base(fields) { }

    /// <summary>The item's name, 3 to 9 characters.</summary>
    public string Name { get; internal init; } = "";

    /// <summary>Whether the item is live (<c>!</c>); a killed item (<c>_</c>) should be removed from the map.</summary>
    public bool IsLive { get; internal init; }
}
