using System.Globalization;
using AprsNet.Packets;

namespace AprsNet.Filters;

/// <summary>
/// One typed part of an APRS-IS filter, such as a range or a buddy list, written in the server syntax by
/// <see cref="ToString"/>. Combine parts with <see cref="AprsFilter.Create(IEnumerable{AprsFilterTerm})"/>.
/// </summary>
/// <remarks>
/// Each factory checks its arguments as <see cref="AprsFilter.Parse"/> would, so every term writes text the parser and
/// APRS-IS servers accept. Numbers are written with invariant culture: coordinates to 4 decimal places (about 11 m)
/// and distances to 1, with trailing zeros dropped. Patterns for <c>b/</c>, <c>o/</c>, <c>d/</c>, <c>e/</c>,
/// <c>g/</c> and <c>u/</c> may use <c>*</c> wildcards.
/// </remarks>
public sealed class AprsFilterTerm
{
    private readonly string _body;

    private AprsFilterTerm(string body, bool isExclusion)
    {
        _body = body;
        IsExclusion = isExclusion;
    }

    /// <summary>Whether this term removes packets (written with a leading <c>-</c>) rather than adding them.</summary>
    public bool IsExclusion { get; }

    /// <summary><c>r/lat/lon/km</c>: positions within <paramref name="km"/> of a point.</summary>
    public static AprsFilterTerm Range(AprsPosition center, double km) =>
        new(
            $"r/{Latitude(center.Latitude, nameof(center))}/{Longitude(center.Longitude, nameof(center))}/{Distance(km)}",
            false
        );

    /// <summary><c>m/km</c>: positions within <paramref name="km"/> of the client's own position.</summary>
    public static AprsFilterTerm MyRange(double km) => new($"m/{Distance(km)}", false);

    /// <summary><c>f/call/km</c>: positions within <paramref name="km"/> of another station's last position.</summary>
    public static AprsFilterTerm FriendRange(string callsign, double km) =>
        new($"f/{Name(callsign, nameof(callsign), wildcards: false)}/{Distance(km)}", false);

    /// <summary><c>a/latN/lonW/latS/lonE</c>: positions inside a box.</summary>
    /// <exception cref="ArgumentException">The north edge is south of the south edge, or the west edge east of the east.</exception>
    public static AprsFilterTerm Area(double north, double west, double south, double east)
    {
        var (n, w, s, e) = (
            Latitude(north, nameof(north)),
            Longitude(west, nameof(west)),
            Latitude(south, nameof(south)),
            Longitude(east, nameof(east))
        );
        if (north < south || east < west)
        {
            throw new ArgumentException(
                "The north-west corner must be north and west of the south-east one; an area cannot cross 180°."
            );
        }

        return new($"a/{n}/{w}/{s}/{e}", false);
    }

    /// <summary><c>a/</c>: positions inside <paramref name="bounds"/>, such as a map view.</summary>
    public static AprsFilterTerm Area(AprsBounds bounds) =>
        Area(
            bounds.NorthEast.Latitude,
            bounds.SouthWest.Longitude,
            bounds.SouthWest.Latitude,
            bounds.NorthEast.Longitude
        );

    /// <summary><c>p/aa/bb</c>: sources whose callsign starts with any of the prefixes.</summary>
    public static AprsFilterTerm Prefix(params IEnumerable<string> prefixes) => List("p", prefixes, wildcards: false);

    /// <summary><c>b/call1/call2</c>: packets from any of the callsigns (the buddy list).</summary>
    public static AprsFilterTerm Buddies(params IEnumerable<string> callsigns) => List("b", callsigns, wildcards: true);

    /// <summary><c>o/name1/name2</c>: objects and items with any of the names. Names with spaces cannot be written.</summary>
    public static AprsFilterTerm Objects(params IEnumerable<string> names) => List("o", names, wildcards: true);

    /// <summary><c>t/types</c>: packets of any of the types.</summary>
    public static AprsFilterTerm Types(AprsFilterTypes types) => new($"t/{TypeLetters(types)}", false);

    /// <summary>
    /// <c>t/types/call/km</c>: packets of any of the types positioned within <paramref name="km"/> of another station.
    /// </summary>
    public static AprsFilterTerm Types(AprsFilterTypes types, string callsign, double km) =>
        new($"t/{TypeLetters(types)}/{Name(callsign, nameof(callsign), wildcards: false)}/{Distance(km)}", false);

    /// <summary>
    /// <c>s/pri/alt/over</c>: positions with any of the symbol codes from the primary table, or from the alternate table
    /// (optionally only with one of the overlays). Each argument is a run of symbol characters, such as <c>&gt;k</c>.
    /// </summary>
    /// <exception cref="ArgumentException">No symbol is given, or one has a space or <c>/</c>.</exception>
    public static AprsFilterTerm Symbols(string primary, string alternate = "", string overlays = "")
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(alternate);
        ArgumentNullException.ThrowIfNull(overlays);
        if (primary.Length == 0 && alternate.Length == 0)
        {
            throw new ArgumentException("A symbol filter needs at least one primary or alternate symbol.");
        }

        foreach (
            var (value, name) in new[]
            {
                (primary, nameof(primary)),
                (alternate, nameof(alternate)),
                (overlays, nameof(overlays)),
            }
        )
        {
            if (value.Any(c => c is '/' or <= ' ' or > '~'))
            {
                throw new ArgumentException("Symbols are printable characters other than '/' and space.", name);
            }
        }

        var body =
            overlays.Length > 0 ? $"s/{primary}/{alternate}/{overlays}"
            : alternate.Length > 0 ? $"s/{primary}/{alternate}"
            : $"s/{primary}";
        return new(body, false);
    }

    /// <summary><c>d/digi1/digi2</c>: packets repeated by any of the digipeaters.</summary>
    public static AprsFilterTerm Digipeaters(params IEnumerable<string> callsigns) =>
        List("d", callsigns, wildcards: true);

    /// <summary><c>e/call1/call2</c>: packets that entered APRS-IS through any of the stations.</summary>
    public static AprsFilterTerm EntryStations(params IEnumerable<string> callsigns) =>
        List("e", callsigns, wildcards: true);

    /// <summary><c>g/call1/call2</c>: messages addressed to any of the callsigns.</summary>
    public static AprsFilterTerm MessagesTo(params IEnumerable<string> addressees) =>
        List("g", addressees, wildcards: true);

    /// <summary><c>u/call1/call2</c>: packets with any of the destinations (unproto calls, such as TOCALLs).</summary>
    public static AprsFilterTerm Destinations(params IEnumerable<string> destinations) =>
        List("u", destinations, wildcards: true);

    /// <summary>
    /// <c>q/con</c>: packets that entered APRS-IS with any of the q-construct letters, such as <c>"RO"</c> for
    /// <c>qAR</c> and <c>qAO</c>. Letters are case-sensitive.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="letters"/> is empty or has a character that is not a letter.</exception>
    public static AprsFilterTerm QConstructs(string letters)
    {
        ArgumentNullException.ThrowIfNull(letters);
        if (letters.Length == 0 || !letters.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                "Give the q-construct letters, such as \"RO\" for qAR and qAO.",
                nameof(letters)
            );
        }

        return new($"q/{letters}", false);
    }

    /// <summary>This term as an exclusion (<c>-r/…</c>), which removes the packets it matches.</summary>
    public AprsFilterTerm Exclude() => IsExclusion ? this : new AprsFilterTerm(_body, true);

    /// <summary>The term in the server syntax, such as <c>r/38.98/-94.67/50</c> or <c>-b/N0CALL*</c>.</summary>
    public override string ToString() => IsExclusion ? "-" + _body : _body;

    /// <summary>The kind letter, such as <c>r</c>.</summary>
    internal char Kind => _body[0];

    private static AprsFilterTerm List(string kind, IEnumerable<string> values, bool wildcards)
    {
        ArgumentNullException.ThrowIfNull(values);
        var names = values.Select(v => Name(v, nameof(values), wildcards)).ToArray();
        if (names.Length == 0)
        {
            throw new ArgumentException("Give at least one value.", nameof(values));
        }

        return new($"{kind}/{string.Join('/', names)}", false);
    }

    private static string Name(string value, string parameter, bool wildcards)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if (value.Length == 0 || value.Any(c => c is '/' or <= ' ' or > '~' || (!wildcards && c == '*')))
        {
            throw new ArgumentException(
                $"'{value}' cannot be written in a filter: it must be printable, with no space or '/'"
                    + (wildcards ? "." : ", and no '*'."),
                parameter
            );
        }

        return value;
    }

    private static string TypeLetters(AprsFilterTypes types)
    {
        var letters = string.Concat("poimqstunw".Where((_, i) => (types & (AprsFilterTypes)(1 << i)) != 0));
        return letters.Length > 0 && (int)types < 1 << 10
            ? letters
            : throw new ArgumentOutOfRangeException(nameof(types), types, "Give at least one known packet type.");
    }

    private static string Latitude(double value, string parameter) =>
        value is >= -90 and <= 90
            ? Number(value, "0.####")
            : throw new ArgumentOutOfRangeException(parameter, value, "A latitude is from -90 to 90.");

    private static string Longitude(double value, string parameter) =>
        value is >= -180 and <= 180
            ? Number(value, "0.####")
            : throw new ArgumentOutOfRangeException(parameter, value, "A longitude is from -180 to 180.");

    private static string Distance(double km) =>
        km is >= 0 and <= 20_038
            ? Number(km, "0.#")
            : throw new ArgumentOutOfRangeException(nameof(km), km, "A distance is from 0 to 20,038 km.");

    // A value that rounds to zero from below would be written "-0".
    private static string Number(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture) is var text && text == "-0" ? "0" : text;
}

/// <summary>Packet types for a <c>t/</c> filter term, by their filter letters.</summary>
[Flags]
public enum AprsFilterTypes
{
    /// <summary>Positions, Mic-E included (<c>p</c>).</summary>
    Positions = 1 << 0,

    /// <summary>Objects (<c>o</c>).</summary>
    Objects = 1 << 1,

    /// <summary>Items (<c>i</c>).</summary>
    Items = 1 << 2,

    /// <summary>Messages, bulletins and telemetry definitions (<c>m</c>).</summary>
    Messages = 1 << 3,

    /// <summary>Queries (<c>q</c>).</summary>
    Queries = 1 << 4,

    /// <summary>Status reports (<c>s</c>).</summary>
    StatusReports = 1 << 5,

    /// <summary>Telemetry (<c>t</c>).</summary>
    Telemetry = 1 << 6,

    /// <summary>User-defined data (<c>u</c>).</summary>
    UserDefined = 1 << 7,

    /// <summary>NWS weather bulletins (<c>n</c>).</summary>
    Nws = 1 << 8,

    /// <summary>Weather (<c>w</c>).</summary>
    Weather = 1 << 9,
}
