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
    /// <exception cref="AprsValidationException">
    /// A part cannot be sent; the message names which and <see cref="AprsValidationException.Code"/> says why.
    /// </exception>
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
    /// <remarks>
    /// A <see cref="MicEReport"/> carries its latitude and message in the destination field, so its packet is sent with
    /// that in place of <see cref="Destination"/>, keeping only the TOCALL's SSID (a Mic-E digipeater path code).
    /// </remarks>
    /// <exception cref="NotSupportedException">The payload type cannot be encoded yet.</exception>
    public AprsPacket Create(AprsPayload payload) => AprsPacket.Create(_source, _destination, _path, payload);

    /// <summary>
    /// Builds a packet from this address and <paramref name="payload"/>, sent with <paramref name="path"/> instead of
    /// the default; <see cref="Path"/> is not changed. An empty path sends the packet to APRS-IS only.
    /// </summary>
    /// <param name="payload">The payload to encode; see <see cref="AprsPacket.Create"/> for the types supported.</param>
    /// <param name="path">The path for this packet.</param>
    /// <exception cref="ArgumentException">
    /// A path entry cannot be sent, or the payload cannot be encoded or would not decode back the same.
    /// </exception>
    /// <remarks>A <see cref="MicEReport"/> replaces the TOCALL; see <see cref="Create(AprsPayload)"/>.</remarks>
    /// <exception cref="NotSupportedException">The payload type cannot be encoded yet.</exception>
    public AprsPacket Create(AprsPayload payload, IEnumerable<PathEntry> path) =>
        AprsPacket.Create(_source, _destination, CheckPath(path, nameof(path)), payload);

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

    /// <summary>A callsign of 1-6 letters or digits with an optional SSID of 1-2 letters or digits.</summary>
    private static Callsign CheckSource(Callsign source, string paramName)
    {
        ArgumentNullException.ThrowIfNull(source, paramName);
        return SourceProblem(source.Value) is null ? source : throw BadSource(source.Value, paramName);
    }

    /// <summary>
    /// Why <paramref name="value"/> cannot send: the code and the offending character's index, or
    /// <see langword="null"/> for a callsign of 1-6 letters or digits with an optional SSID of 1-2 letters or digits.
    /// </summary>
    private static (AprsValidationErrorCode Code, int? Index)? SourceProblem(string? value)
    {
        if (Callsign.Check(value, "", pathEntry: false) is { } header)
        {
            return (header.Code, header.Index);
        }

        var dash = value!.IndexOf('-', StringComparison.Ordinal);
        var @base = dash < 0 ? value : value[..dash];
        var bad = @base.AsSpan().IndexOfAnyExcept(_lettersAndDigits);
        if (@base.Length is < 1 or > 6)
        {
            return (AprsValidationErrorCode.CallsignInvalidLength, null);
        }

        if (bad >= 0)
        {
            return (AprsValidationErrorCode.CallsignInvalidCharacter, bad);
        }

        var ssid = value[(dash + 1)..];
        return dash >= 0 && (ssid.Length is < 1 or > 2 || ssid.AsSpan().IndexOfAnyExcept(_lettersAndDigits) >= 0)
            ? (AprsValidationErrorCode.CallsignInvalidSsid, dash + 1)
            : null;
    }

    private static readonly System.Buffers.SearchValues<char> _lettersAndDigits = System.Buffers.SearchValues.Create(
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
    );

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
        catch (AprsValidationException ex)
        {
            throw BadPathEntry(entry, ex.Code, ex.Index);
        }
    }

    private static PathEntry[] CheckPath(IEnumerable<PathEntry> path, string paramName)
    {
        ArgumentNullException.ThrowIfNull(path, paramName);
        PathEntry[] entries = [.. path];
        foreach (var entry in entries)
        {
            if (Callsign.Check(entry.Station?.Value, paramName, pathEntry: true) is { } error)
            {
                throw BadPathEntry(entry.Station?.Value, error.Code, error.Index, paramName);
            }
        }

        return entries.Length <= AprsPacket.MaxPathEntries
            ? entries
            : throw new AprsValidationException(
                AprsValidationErrorCode.PathTooLong,
                $"The path has {entries.Length} entries; a path holds at most {AprsPacket.MaxPathEntries}.",
                paramName,
                string.Join(',', entries)
            );
    }

    private static AprsValidationException BadSource(string? value, string paramName) =>
        new(
            SourceProblem(value)?.Code ?? AprsValidationErrorCode.CallsignInvalidCharacter,
            $"The source '{value}' is not a callsign that can send: use 1 to 6 letters or digits, optionally followed "
                + "by '-' and an SSID of 1 or 2 letters or digits (N0CALL-9), without the '*' repeated marker.",
            paramName,
            value,
            SourceProblem(value)?.Index
        );

    private static AprsValidationException BadDestination(string? value, string paramName) =>
        new(
            AprsValidationErrorCode.DestinationInvalid,
            $"The destination '{value}' is not a TOCALL: use 1 to 6 upper-case letters or digits, optionally followed "
                + "by an SSID from 0 to 15 (APZ123).",
            paramName,
            value
        );

    private static AprsValidationException BadPathEntry(
        string? value,
        AprsValidationErrorCode code,
        int? index,
        string paramName = "path"
    ) =>
        new(
            code,
            $"The path entry '{value}' is not a usable callsign or alias: use 1 to 9 printable ASCII characters "
                + "without spaces, '>', ':' or ',' (WIDE1-1), with a trailing '*' only once repeated.",
            paramName,
            value,
            index
        );
}
