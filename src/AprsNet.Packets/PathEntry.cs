namespace AprsNet.Packets;

/// <summary>One entry of a packet's digipeater and APRS-IS path.</summary>
/// <param name="Station">The entry without its has-been-repeated marker.</param>
/// <param name="HasBeenRepeated">Whether the entry ended with <c>*</c>.</param>
/// <param name="Kind">What the entry is.</param>
public readonly record struct PathEntry(Callsign Station, bool HasBeenRepeated, PathEntryKind Kind)
{
    /// <summary>
    /// Creates a path entry from its wire form, for example <c>WIDE1-1</c>, or <c>WIDE1-1*</c> once repeated. The kind
    /// is classified as the parser would; an APRS-IS server name after a q-construct is classified when the packet is
    /// built.
    /// </summary>
    /// <exception cref="AprsValidationException">
    /// The entry is not a usable callsign or alias: <see cref="AprsValidationErrorCode.PathEntryInvalidLength"/> or
    /// <see cref="AprsValidationErrorCode.PathEntryInvalidCharacter"/>.
    /// </exception>
    public static PathEntry Parse(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var repeated = entry.EndsWith('*');
        var name = repeated ? entry[..^1] : entry;
        if (Callsign.Check(name, nameof(entry), pathEntry: true) is { } error)
        {
            throw error;
        }

        var station = Callsign.Parse(name);
        return new PathEntry(station, repeated, AprsPacket.Classify(station.Value, serverFollows: false));
    }

    /// <summary>The entry as it appears on the wire, for example <c>WIDE1-1*</c>.</summary>
    public override string ToString() => HasBeenRepeated ? Station.Value + "*" : Station.Value;
}
