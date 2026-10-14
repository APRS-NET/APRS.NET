namespace AprsNet.Packets;

/// <summary>
/// An item (<c>)</c>, APRS101 ch. 11): like an object but for things that do not change over time, with a 3–9
/// character name and no timestamp. It is live or killed.
/// </summary>
public sealed class ItemReport : PositionedPayload
{
    /// <summary>
    /// Creates an item to send with <see cref="AprsPacket.Create"/>: a name and the position body that position reports
    /// have, without a timestamp. It is live unless <see cref="IsLive"/> is set to <see langword="false"/>.
    /// </summary>
    public ItemReport() { }

    internal ItemReport(PositionFields fields)
        : base(fields) { }

    /// <summary>
    /// The item's name, 3 to 9 characters. A new name is printable ASCII without <c>!</c> or <c>_</c>, which end the
    /// name on the wire.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Whether the item is live (<c>!</c>); a killed item (<c>_</c>) should be removed from the map. To kill an item,
    /// send it again with this set to <see langword="false"/>. New items are live.
    /// </summary>
    public bool IsLive { get; init; } = true;
}
