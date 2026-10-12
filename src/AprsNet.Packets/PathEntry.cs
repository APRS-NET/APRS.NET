namespace AprsNet.Packets;

/// <summary>One entry of a packet's digipeater and APRS-IS path.</summary>
/// <param name="Station">The entry without its has-been-repeated marker.</param>
/// <param name="HasBeenRepeated">Whether the entry ended with <c>*</c>.</param>
/// <param name="Kind">What the entry is.</param>
public readonly record struct PathEntry(Callsign Station, bool HasBeenRepeated, PathEntryKind Kind)
{
    /// <summary>The entry as it appears on the wire, for example <c>WIDE1-1*</c>.</summary>
    public override string ToString() => HasBeenRepeated ? Station.Value + "*" : Station.Value;
}
