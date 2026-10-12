using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace AprsNet.Packets;

/// <summary>
/// An APRS packet in TNC2 format (<c>SOURCE&gt;DESTINATION[,PATH...]:INFORMATION</c>), with the exact bytes it was
/// parsed from. Create one with <see cref="Parse(ReadOnlySpan{byte})"/> or <see cref="Parse(string)"/>; parsing never
/// throws for malformed data, it records <see cref="Issues"/> instead. Instances are immutable and thread-safe.
/// </summary>
public sealed partial class AprsPacket
{
    /// <summary>Path entries beyond this many are not decoded: 8 digipeaters plus a q-construct and its station.</summary>
    internal const int MaxPathEntries = 10;

    /// <summary>Information bytes beyond this many are not decoded; APRS-IS servers accept 512-byte lines.</summary>
    internal const int MaxInformationLength = 512;

    private readonly byte[] _raw;
    private string? _text;

    private AprsPacket(
        byte[] raw,
        Callsign? source,
        Callsign? destination,
        IReadOnlyList<PathEntry>? path,
        ReadOnlyMemory<byte> information,
        PacketType type,
        IReadOnlyList<ParseIssue> issues
    )
    {
        _raw = raw;
        Source = source;
        Destination = destination;
        Path = path;
        Information = information;
        Type = type;
        Issues = issues;
        Outcome =
            issues.Any(i => i.Severity == ParseIssueSeverity.Error) ? ParseOutcome.Malformed
            : Payload is null ? ParseOutcome.Unsupported
            : ParseOutcome.Parsed;
    }

    /// <summary>The exact input, without its line ending.</summary>
    public ReadOnlyMemory<byte> Raw => _raw;

    /// <summary>Whether the line has a <c>SOURCE&gt;DESTINATION</c> header with a non-empty source.</summary>
    [MemberNotNullWhen(true, nameof(Source), nameof(Destination), nameof(Path))]
    public bool HasHeader => Source is not null;

    /// <summary>The station that sent the packet, or <see langword="null"/> without a header.</summary>
    public Callsign? Source { get; }

    /// <summary>
    /// The destination: usually a device identifier (TOCALL), but Mic-E packets carry their latitude here. Empty
    /// when the header has no destination, <see langword="null"/> without a header.
    /// </summary>
    public Callsign? Destination { get; }

    /// <summary>Digipeaters, aliases and APRS-IS entries, in order; empty when there are none.</summary>
    public IReadOnlyList<PathEntry>? Path { get; }

    /// <summary>Everything after the first <c>:</c>; empty without a header.</summary>
    public ReadOnlyMemory<byte> Information { get; }

    /// <summary>The kind of packet, from the first byte of <see cref="Information"/>.</summary>
    public PacketType Type { get; }

    /// <summary>The decoded information field, or <see langword="null"/> unless <see cref="Outcome"/> is <see cref="ParseOutcome.Parsed"/>.</summary>
    public AprsPayload? Payload { get; }

    /// <summary>How far parsing got.</summary>
    public ParseOutcome Outcome { get; }

    /// <summary>Problems found while parsing, in the order they were found; empty for a clean packet.</summary>
    public IReadOnlyList<ParseIssue> Issues { get; }

    /// <summary>
    /// Parses one TNC2-format line. A trailing CR, LF or CRLF is dropped; every other byte is kept. Never throws for
    /// malformed data: check <see cref="Outcome"/> and <see cref="Issues"/>.
    /// </summary>
    public static AprsPacket Parse(ReadOnlySpan<byte> line) =>
        Create(line[..^LineEndingLength(line[^Math.Min(2, line.Length)..])].ToArray());

    /// <summary>
    /// Parses one TNC2-format line held as Latin-1 text, as <c>AprsIsClient</c> returns it: each character is one
    /// byte. A trailing CR, LF or CRLF is dropped. Never throws for malformed data: check <see cref="Outcome"/> and
    /// <see cref="Issues"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="line"/> holds a character above U+00FF, so it was not decoded as Latin-1 and bytes are lost.
    /// </exception>
    public static AprsPacket Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var wide = line.AsSpan().IndexOfAnyExceptInRange('\0', 'ÿ');
        if (wide >= 0)
        {
            throw new ArgumentException(
                $"Character U+{(int)line[wide]:X4} at index {wide} is not Latin-1. Read APRS text as Latin-1 (one "
                    + "character per byte), or pass the bytes.",
                nameof(line)
            );
        }

        var ending = LineEndingLength(line.AsSpan(line.Length - Math.Min(2, line.Length)));
        return Create(Encoding.Latin1.GetBytes(line, 0, line.Length - ending));
    }

    /// <summary>A new array holding <see cref="Raw"/>.</summary>
    public byte[] GetBytes() => (byte[])_raw.Clone();

    /// <summary>The original line: <see cref="Raw"/> as Latin-1 text, one character per byte.</summary>
    public override string ToString() => _text ??= Encoding.Latin1.GetString(_raw);

    private static int LineEndingLength(ReadOnlySpan<byte> end) =>
        end.EndsWith("\r\n"u8) ? 2
        : end.Length > 0 && end[^1] is (byte)'\r' or (byte)'\n' ? 1
        : 0;

    private static int LineEndingLength(ReadOnlySpan<char> end) =>
        end.EndsWith("\r\n", StringComparison.Ordinal) ? 2
        : end.Length > 0 && end[^1] is '\r' or '\n' ? 1
        : 0;

    private static AprsPacket Create(byte[] raw)
    {
        var issues = new List<ParseIssue>();
        var span = raw.AsSpan();
        var colon = span.IndexOf((byte)':');
        var gt = colon < 0 ? -1 : span[..colon].IndexOf((byte)'>');
        if (gt <= 0)
        {
            issues.Add(Error(gt == 0 ? ParseIssueCode.EmptySource : ParseIssueCode.MissingHeader, 0));
            return new AprsPacket(raw, null, null, null, default, PacketType.Unknown, issues);
        }

        var source = Station(raw, 0, gt, issues);
        Callsign? destination = null;
        var path = new List<PathEntry>();
        var serverFollows = false;
        var start = gt + 1;
        while (true)
        {
            var comma = span[start..colon].IndexOf((byte)',');
            var end = comma < 0 ? colon : start + comma;
            if (destination is null)
            {
                if (end == start)
                {
                    issues.Add(Error(ParseIssueCode.EmptyDestination, start));
                }

                destination = Station(raw, start, end, issues);
            }
            else if (path.Count == MaxPathEntries)
            {
                issues.Add(Error(ParseIssueCode.TooManyPathEntries, start));
                break;
            }
            else if (end == start || (end == start + 1 && raw[start] == (byte)'*'))
            {
                issues.Add(Error(ParseIssueCode.EmptyPathEntry, start));
            }
            else
            {
                var repeated = raw[end - 1] == (byte)'*';
                var station = Station(raw, start, repeated ? end - 1 : end, issues);
                var kind = Classify(station.Value, serverFollows);
                serverFollows |= kind == PathEntryKind.QConstruct && station.Value[2] is 'C' or 'X' or 'U' or 'I';
                path.Add(new PathEntry(station, repeated, kind));
            }

            if (comma < 0)
            {
                break;
            }

            start = end + 1;
        }

        var information = raw.AsMemory(colon + 1);
        if (information.IsEmpty)
        {
            issues.Add(Error(ParseIssueCode.EmptyInformation, colon + 1));
        }
        else if (information.Length > MaxInformationLength)
        {
            issues.Add(
                new ParseIssue(
                    ParseIssueCode.InformationTooLong,
                    ParseIssueSeverity.Warning,
                    colon + 1 + MaxInformationLength
                )
            );
        }

        return new AprsPacket(raw, source, destination, path, information, TypeOf(information.Span), issues);
    }

    private static ParseIssue Error(ParseIssueCode code, int offset) => new(code, ParseIssueSeverity.Error, offset);

    private static Callsign Station(byte[] raw, int start, int end, List<ParseIssue> issues)
    {
        var bytes = raw.AsSpan(start, end - start);
        var invalid = bytes.IndexOfAnyExceptInRange((byte)0x21, (byte)0x7E);
        if (invalid >= 0)
        {
            issues.Add(new ParseIssue(ParseIssueCode.InvalidCallsign, ParseIssueSeverity.Warning, start + invalid));
        }

        return new Callsign(Encoding.Latin1.GetString(bytes));
    }

    private static PathEntryKind Classify(string entry, bool serverFollows) =>
        QConstruct().IsMatch(entry) ? PathEntryKind.QConstruct
        : serverFollows ? PathEntryKind.Server
        : Alias().IsMatch(entry) ? PathEntryKind.Alias
        : PathEntryKind.Station;

    private static PacketType TypeOf(ReadOnlySpan<byte> information) =>
        information.IsEmpty
            ? PacketType.Unknown
            : information[0] switch
            {
                (byte)'!' or (byte)'=' or (byte)'/' or (byte)'@' => PacketType.Position,
                (byte)'`' or (byte)'\'' or 0x1C or 0x1D => PacketType.MicE,
                (byte)';' => PacketType.Object,
                (byte)')' => PacketType.Item,
                (byte)':' => PacketType.Message,
                (byte)'>' => PacketType.Status,
                (byte)'_' => PacketType.Weather,
                (byte)'}' => PacketType.ThirdParty,
                (byte)'T' when information.Length > 1 && information[1] == (byte)'#' => PacketType.Telemetry,
                _ => PacketType.Unknown,
            };

    [GeneratedRegex("^qA[A-Za-z]$")]
    private static partial Regex QConstruct();

    // Generic hops from APRS101 (RELAY, ECHO, GATE, WIDE, TRACE) and the New-N paradigm (WIDEn-N, TRACEn-N, regional
    // XXn-N), plus the APRS-IS markers. A regional alias cannot be a callsign: callsigns end in letters.
    [GeneratedRegex(
        "^(?:RELAY|ECHO|GATE|TCPIP|TCPXX|NOGATE|RFONLY|(?:WIDE|TRACE|TEMP)[1-7]?(?:-[0-7])?|[A-Z]{2,5}[1-7]-[0-7])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex Alias();
}
