using System.Text;

namespace AprsNet.Packets;

/// <summary>
/// Who a station sends as: the header of its outgoing packets, <c>SOURCE&gt;DESTINATION,PATH</c>. An application
/// usually sends every packet with the same source, the same destination (its TOCALL) and the same path, so it makes
/// one address and builds each packet with <see cref="Create(AprsPayload)"/>. The parts are checked when the address
/// is made; <c>with</c> makes variants, for example <c>address with { Path = [] }</c> for APRS-IS. The address is
/// immutable, so one instance can be shared across threads.
/// </summary>
public sealed record PacketAddress
{
    private readonly Callsign _source = null!;
    private readonly Callsign _destination = null!;
    private readonly IReadOnlyList<PathEntry> _path = null!;

    /// <summary>Creates an address from parts that are already parsed.</summary>
    /// <param name="source">The sending station, for example <c>N0CALL-9</c>.</param>
    /// <param name="destination">The TOCALL that identifies the software, for example <c>APZ123</c>.</param>
    /// <param name="path">The default path, for example <c>WIDE1-1,WIDE2-1</c> on RF, or none for APRS-IS.</param>
    /// <exception cref="ArgumentException">A part cannot be sent; the message names which.</exception>
    public PacketAddress(Callsign source, Callsign destination, IEnumerable<PathEntry> path)
    {
        _source = CheckSource(source, nameof(source));
        _destination = CheckDestination(destination, nameof(destination));
        _path = CheckPath(path, nameof(path));
    }

    /// <summary>
    /// Creates an address from its wire forms, parsed with <see cref="Callsign.Parse"/> and
    /// <see cref="PathEntry.Parse"/>.
    /// </summary>
    /// <param name="source">The sending station, for example <c>N0CALL-9</c>.</param>
    /// <param name="destination">The TOCALL that identifies the software, for example <c>APZ123</c>.</param>
    /// <param name="path">The default path entries, for example <c>["WIDE1-1", "WIDE2-1"]</c>, or none for APRS-IS.</param>
    /// <exception cref="ArgumentException">A part cannot be sent; the message names which.</exception>
    public static PacketAddress Parse(string source, string destination, IEnumerable<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new PacketAddress(
            Callsign.TryParse(source, out var from) ? from : throw BadSource(source, nameof(source)),
            Callsign.TryParse(destination, out var to) ? to : throw BadDestination(destination, nameof(destination)),
            path.Select(ParsePathEntry)
        );
    }

    /// <summary>
    /// The sending station: a callsign of one to six letters or digits, optionally followed by <c>-</c> and an SSID of
    /// one or two letters or digits. APRS-IS also accepts SSIDs that AX.25 cannot carry, such as <c>-L</c>; check
    /// <see cref="Callsign.IsAx25"/> before sending on RF.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not such a callsign.</exception>
    public Callsign Source
    {
        get => _source;
        init => _source = CheckSource(value, nameof(Source));
    }

    /// <summary>
    /// The destination field: the TOCALL that identifies the sending software, such as <c>APZ123</c>. It must fit an
    /// AX.25 address (<see cref="Callsign.IsAx25"/>), as every TOCALL does.
    /// </summary>
    /// <exception cref="ArgumentException">The value does not fit an AX.25 address.</exception>
    public Callsign Destination
    {
        get => _destination;
        init => _destination = CheckDestination(value, nameof(Destination));
    }

    /// <summary>
    /// The path packets are sent with unless <see cref="Create(AprsPayload, IEnumerable{PathEntry})"/> is given
    /// another: digipeater aliases such as <c>WIDE1-1</c> on RF, or empty for APRS-IS. At most 10 entries.
    /// </summary>
    /// <exception cref="ArgumentException">An entry is not a usable callsign or alias, or there are too many.</exception>
    public IReadOnlyList<PathEntry> Path
    {
        get => _path;
        init => _path = CheckPath(value, nameof(Path));
    }

    /// <summary>Builds a packet from this address and <paramref name="payload"/>, with the default path.</summary>
    /// <param name="payload">The payload to encode; see <see cref="AprsPacket.Create"/> for the types supported.</param>
    /// <exception cref="ArgumentException">The payload cannot be encoded, or would not decode back the same.</exception>
    /// <exception cref="NotSupportedException">
    /// The payload type cannot be encoded yet. That includes <see cref="MicEReport"/>: a Mic-E packet carries its
    /// latitude in the destination field in place of the TOCALL, so once Mic-E encoding is supported this method will
    /// write that destination instead of <see cref="Destination"/>.
    /// </exception>
    public AprsPacket Create(AprsPayload payload) => Build(payload, _path);

    /// <summary>
    /// Builds a packet from this address and <paramref name="payload"/>, sent with <paramref name="path"/> instead of
    /// the default; <see cref="Path"/> is not changed. An empty path sends the packet to APRS-IS only.
    /// </summary>
    /// <param name="payload">The payload to encode; see <see cref="AprsPacket.Create"/> for the types supported.</param>
    /// <param name="path">The path for this packet.</param>
    /// <exception cref="ArgumentException">
    /// A path entry cannot be sent, or the payload cannot be encoded or would not decode back the same.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The payload type cannot be encoded yet, including <see cref="MicEReport"/>; see <see cref="Create(AprsPayload)"/>.
    /// </exception>
    public AprsPacket Create(AprsPayload payload, IEnumerable<PathEntry> path) =>
        Build(payload, CheckPath(path, nameof(path)));

    /// <summary>Whether both addresses have the same source, destination and path entries.</summary>
    public bool Equals(PacketAddress? other) =>
        other is not null
        && _source == other._source
        && _destination == other._destination
        && _path.SequenceEqual(other._path);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_source);
        hash.Add(_destination);
        foreach (var entry in _path)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }

    /// <summary>The header without the colon, for example <c>N0CALL-9&gt;APZ123,WIDE1-1,WIDE2-1</c>.</summary>
    public override string ToString()
    {
        var header = new StringBuilder(_source.Value).Append('>').Append(_destination.Value);
        foreach (var entry in _path)
        {
            header.Append(',').Append(entry);
        }

        return header.ToString();
    }

    private AprsPacket Build(AprsPayload payload, IEnumerable<PathEntry> path)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload is MicEReport
            ? throw new NotSupportedException(
                "Encoding Mic-E reports is not supported yet. A Mic-E packet carries its latitude in the destination "
                    + "field, so it will replace the address's TOCALL once it is."
            )
            : AprsPacket.Create(_source, _destination, path, payload);
    }

    /// <summary>A callsign of 1-6 letters or digits with an optional SSID of 1-2 letters or digits.</summary>
    private static Callsign CheckSource(Callsign source, string paramName)
    {
        ArgumentNullException.ThrowIfNull(source, paramName);
        var @base = source.Base;
        var ssid = source.Ssid;
        return
            Callsign.TryParse(source.Value, out _)
            && @base.Length is >= 1 and <= 6
            && @base.All(char.IsAsciiLetterOrDigit)
            && (
                !source.Value.Contains('-', StringComparison.Ordinal)
                || (ssid.Length is 1 or 2 && ssid.All(char.IsAsciiLetterOrDigit))
            )
            ? source
            : throw BadSource(source.Value, paramName);
    }

    /// <summary>A TOCALL: any callsign that fits an AX.25 address.</summary>
    private static Callsign CheckDestination(Callsign destination, string paramName)
    {
        ArgumentNullException.ThrowIfNull(destination, paramName);
        return Callsign.TryParse(destination.Value, out _) && destination.IsAx25
            ? destination
            : throw BadDestination(destination.Value, paramName);
    }

    private static PathEntry ParsePathEntry(string entry)
    {
        try
        {
            return PathEntry.Parse(entry);
        }
        catch (ArgumentException ex)
        {
            throw BadPathEntry(entry, ex);
        }
    }

    private static PathEntry[] CheckPath(IEnumerable<PathEntry> path, string paramName)
    {
        ArgumentNullException.ThrowIfNull(path, paramName);
        PathEntry[] entries = [.. path];
        foreach (var entry in entries)
        {
            if (entry.Station is null || !Callsign.TryParse(entry.Station.Value, out _))
            {
                throw BadPathEntry(entry.Station?.Value, null, paramName);
            }
        }

        return entries.Length <= AprsPacket.MaxPathEntries
            ? entries
            : throw new ArgumentException(
                $"The path has {entries.Length} entries; a path holds at most {AprsPacket.MaxPathEntries}.",
                paramName
            );
    }

    private static ArgumentException BadSource(string? value, string paramName) =>
        new(
            $"The source '{value}' is not a callsign that can send: use 1 to 6 letters or digits, optionally followed "
                + "by '-' and an SSID of 1 or 2 letters or digits (N0CALL-9), without the '*' repeated marker.",
            paramName
        );

    private static ArgumentException BadDestination(string? value, string paramName) =>
        new(
            $"The destination '{value}' is not a TOCALL: use 1 to 6 upper-case letters or digits, optionally followed "
                + "by an SSID from 0 to 15 (APZ123).",
            paramName
        );

    private static ArgumentException BadPathEntry(string? value, Exception? inner, string paramName = "path") =>
        new(
            $"The path entry '{value}' is not a usable callsign or alias: use 1 to 9 printable ASCII characters "
                + "without spaces, '>', ':' or ',' (WIDE1-1), with a trailing '*' only once repeated.",
            paramName,
            inner
        );
}
