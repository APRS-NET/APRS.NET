using System.Globalization;

namespace AprsNet.Packets;

/// <summary>What a query asks for (APRS101 ch. 15), from its query type.</summary>
public enum QueryKind
{
    /// <summary>A query type this library does not know, such as <c>?TELEM?</c>; APRS101 says to ignore it.</summary>
    Unknown,

    /// <summary><c>APRS</c>: every station answers with its position and status.</summary>
    PositionAndStatus,

    /// <summary><c>IGATE</c>: every IGate answers with its capabilities.</summary>
    IGate,

    /// <summary><c>WX</c>: every weather station answers with a weather report, and its position.</summary>
    Weather,

    /// <summary><c>APRSP</c>: the station's position.</summary>
    Position,

    /// <summary><c>APRSS</c>: the station's status.</summary>
    Status,

    /// <summary><c>APRSD</c>: the stations it has heard direct.</summary>
    HeardDirect,

    /// <summary><c>APRSH</c>: whether it has heard a station, and how often in each of the last 8 hours.</summary>
    Heard,

    /// <summary><c>APRSM</c>: the messages it holds for the querying station, unacknowledged or undelivered.</summary>
    Messages,

    /// <summary><c>APRSO</c>: its objects.</summary>
    Objects,

    /// <summary><c>APRST</c> or <c>PING</c>: the path by which the query reached it.</summary>
    Trace,
}

/// <summary>
/// The area of a general query (APRS101 p. 78): only stations within <see cref="RadiusMiles"/> of
/// <see cref="Center"/> answer. Sent as <c>?APRS? 34.02,-117.15,0200</c>: degrees, then the radius in whole miles.
/// </summary>
/// <param name="Center">The centre of the area.</param>
/// <param name="RadiusMiles">The radius in statute miles, 0 to 9999.</param>
public readonly record struct QueryFootprint(AprsPosition Center, int RadiusMiles)
{
    private const double KmPerMile = 1.609344;

    /// <summary>Whether <paramref name="position"/> is inside the area, on the great circle.</summary>
    public bool Contains(AprsPosition position) => Geodesy.DistanceKm(Center, position) <= RadiusMiles * KmPerMile;
}

/// <summary>
/// A general query (<c>?</c>, APRS101 ch. 15), sent to every station: <c>?APRS?</c>, <c>?IGATE?</c> or <c>?WX?</c>,
/// optionally with a <see cref="Footprint"/>. Queries directed at one station are messages instead: a
/// <see cref="MessagePayload"/> of kind <see cref="MessageKind.Query"/>.
/// </summary>
public sealed class QueryReport : AprsPayload
{
    /// <summary>
    /// Creates a general query to send with <see cref="AprsPacket.Create"/>. Set <see cref="QueryType"/>, and
    /// optionally <see cref="Footprint"/>.
    /// </summary>
    public QueryReport() { }

    /// <summary>The query type between the question marks: <c>APRS</c>, <c>IGATE</c> or <c>WX</c>. Letters and digits.</summary>
    public string QueryType { get; init; } = "";

    /// <summary>What the query asks for, from <see cref="QueryType"/>.</summary>
    public QueryKind Kind => QueryKinds.Of(QueryType);

    /// <summary>The area the query is for; <see langword="null"/> asks every station that hears it.</summary>
    public QueryFootprint? Footprint { get; init; }

    /// <summary>
    /// The text after the closing <c>?</c> as sent, such as <c>34.02,-117.15,0200</c>; <see langword="null"/> when
    /// nothing follows. When it is there but <see cref="Footprint"/> is <see langword="null"/>, the area couldn't be read.
    /// </summary>
    public string? Argument { get; internal init; }
}

/// <summary>Reads query types and the <c>?</c> data type.</summary>
internal static class QueryKinds
{
    public static QueryKind Of(string? queryType) =>
        queryType?.ToUpperInvariant() switch
        {
            "APRS" => QueryKind.PositionAndStatus,
            "IGATE" => QueryKind.IGate,
            "WX" => QueryKind.Weather,
            "APRSP" => QueryKind.Position,
            "APRSS" => QueryKind.Status,
            "APRSD" => QueryKind.HeardDirect,
            "APRSH" => QueryKind.Heard,
            "APRSM" => QueryKind.Messages,
            "APRSO" => QueryKind.Objects,
            "APRST" or "PING" => QueryKind.Trace,
            _ => QueryKind.Unknown,
        };

    public static bool IsQueryType(ReadOnlySpan<char> type) =>
        type.Length > 0 && !type.ContainsAnyExcept(_lettersAndDigits);

    private static readonly System.Buffers.SearchValues<char> _lettersAndDigits = System.Buffers.SearchValues.Create(
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
    );

    /// <summary>Decodes <c>?TYPE?</c> and an optional footprint; <paramref name="offset"/> is where <c>?</c> is in Raw.</summary>
    public static QueryReport? Decode(ReadOnlyMemory<byte> information, int offset, List<ParseIssue> issues)
    {
        var text = System.Text.Encoding.Latin1.GetString(information.Span);
        var close = text.IndexOf('?', 1);
        if (close < 0 || !IsQueryType(text.AsSpan(1, close - 1)))
        {
            issues.Add(new ParseIssue(ParseIssueCode.InvalidQuery, ParseIssueSeverity.Error, offset));
            return null;
        }

        var argument = text[(close + 1)..].Trim(' ');
        QueryFootprint? footprint = null;
        if (argument.Length > 0)
        {
            footprint = Footprint(argument);
            if (footprint is null)
            {
                issues.Add(
                    new ParseIssue(ParseIssueCode.InvalidQueryFootprint, ParseIssueSeverity.Warning, offset + close + 1)
                );
            }
        }

        return new QueryReport
        {
            QueryType = text[1..close],
            Footprint = footprint,
            Argument = argument.Length > 0 ? argument : null,
        };
    }

    /// <summary>Reads <c>lat,long,radius</c>: degrees, negative south and west, and the radius in up to 4 digits.</summary>
    private static QueryFootprint? Footprint(string text)
    {
        var parts = text.Split(',');
        const NumberStyles degrees = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return
            parts.Length == 3
            && double.TryParse(parts[0], degrees, CultureInfo.InvariantCulture, out var latitude)
            && latitude is >= -90 and <= 90
            && double.TryParse(parts[1], degrees, CultureInfo.InvariantCulture, out var longitude)
            && longitude is >= -180 and <= 180
            && parts[2].Length is >= 1 and <= 4
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var radius)
            ? new QueryFootprint(new AprsPosition(latitude, longitude), radius)
            : null;
    }

    public static byte[] Encode(QueryReport query)
    {
        if (!IsQueryType(query.QueryType))
        {
            throw new ArgumentException("A query type must be letters or digits, such as APRS, IGATE or WX.");
        }

        var text = $"?{query.QueryType}?";
        if (query.Footprint is { } footprint)
        {
            var (center, radius) = (footprint.Center, footprint.RadiusMiles);
            if (radius is < 0 or > 9999)
            {
                throw new ArgumentException("A query footprint's radius is 0 to 9999 miles.");
            }

            // APRS101 p. 78: a positive latitude is sent with a leading space, as its sign.
            text += string.Create(
                CultureInfo.InvariantCulture,
                $"{(center.Latitude >= 0 ? " " : "")}{center.Latitude:0.####},{center.Longitude:0.####},{radius:0000}"
            );
        }

        return System.Text.Encoding.ASCII.GetBytes(text);
    }

    public static string? Mismatch(QueryReport e, QueryReport a) =>
        e.QueryType != a.QueryType ? "the query type does not survive encoding"
        : (e.Footprint is null) != (a.Footprint is null)
        || e.Footprint is { } sent
            && a.Footprint is { } read
            && (
                sent.RadiusMiles != read.RadiusMiles
                || Math.Abs(sent.Center.Latitude - read.Center.Latitude) > 0.00005
                || Math.Abs(sent.Center.Longitude - read.Center.Longitude) > 0.00005
            )
            ? "the footprint does not survive encoding"
        : null;
}
