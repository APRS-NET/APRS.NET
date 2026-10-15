using System.Globalization;
using AprsNet.Packets;

namespace AprsNet.Filters;

/// <summary>
/// An APRS-IS filter expression, such as <c>r/38.98/-94.67/50 t/m -b/N0CALL*</c>, evaluated locally. The syntax is the
/// server-side filter syntax of javAPRSSrvr and aprsc, so a filter a user already has for APRS-IS works on packets from
/// any source.
/// </summary>
/// <remarks>
/// <para>
/// Terms are separated by spaces. A packet matches when any term matches it and no excluding term (one starting with
/// <c>-</c>) does; a filter with only excluding terms matches nothing, as on APRS-IS.
/// </para>
/// <para>
/// Supported terms: <c>r/lat/lon/km</c> (range), <c>a/latN/lonW/latS/lonE</c> (area), <c>p/aa/bb</c> (source prefix),
/// <c>b/call1/call2</c> (source), <c>o/name1/name2</c> (object and item names), <c>t/types</c> and
/// <c>t/types/call/km</c> (packet type), <c>s/pri/alt/over</c> (symbol), <c>d/digi1/digi2</c> (digipeater),
/// <c>e/call1/call2</c> (entry station), <c>g/call1/call2</c> (message addressee), <c>u/call1/call2</c>
/// (destination), <c>q/con</c> (q-construct), <c>m/km</c> (range from my position) and <c>f/call/km</c> (range from a
/// friend). <c>b</c>, <c>o</c>, <c>d</c>, <c>e</c>, <c>g</c> and <c>u</c> accept <c>*</c> wildcards. Callsigns and
/// names compare ignoring case; q-construct letters do not (<c>qAr</c> and <c>qAR</c> differ).
/// </para>
/// <para>
/// Terms that need to know where stations are (<c>m/</c>, <c>f/</c> and <c>t/…/call/km</c>) look them up through an
/// <see cref="AprsFilterContext"/>, and match nothing without one.
/// </para>
/// </remarks>
public sealed class AprsFilter
{
    private readonly FilterTerm[] _include;
    private readonly FilterTerm[] _exclude;
    private readonly string _text;

    private AprsFilter(FilterTerm[] include, FilterTerm[] exclude, string text)
    {
        _include = include;
        _exclude = exclude;
        _text = text;
    }

    /// <summary>A filter with no terms, which matches nothing.</summary>
    public static AprsFilter Empty { get; } = new([], [], "");

    /// <summary>
    /// The most range (<c>r/</c>) and friend-range (<c>f/</c>) terms an APRS-IS server accepts in one filter: 9 of each
    /// (aprs-is.net, "Server-side Filter Commands").
    /// </summary>
    public const int ServerTermLimit = 9;

    /// <summary>
    /// A filter of the given terms, such as a range around a map view and a buddy list. It is the filter
    /// <see cref="Parse"/> gives for its <see cref="ToString"/> text, so it can be sent to APRS-IS or evaluated here.
    /// </summary>
    /// <exception cref="ArgumentNullException">A term is <see langword="null"/>.</exception>
    public static AprsFilter Create(params IEnumerable<AprsFilterTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var text = string.Join(' ', terms.Select(t => t?.ToString() ?? throw new ArgumentNullException(nameof(terms))));

        // Every term writes text the parser accepts, so a failure here is a bug in AprsFilterTerm.
        return TryParse(text, out var filter, out var error)
            ? filter
            : throw new InvalidOperationException($"A built filter did not parse: {error}");
    }

    /// <summary>Parses a filter expression.</summary>
    /// <exception cref="FormatException"><paramref name="text"/> has a term that is not a valid filter.</exception>
    public static AprsFilter Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out var filter, out var error) ? filter : throw new FormatException(error);
    }

    /// <summary>
    /// Parses a filter expression without throwing. On failure, <paramref name="error"/> says which term is wrong and
    /// why, in a sentence that can be shown to the user.
    /// </summary>
    public static bool TryParse(
        string? text,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AprsFilter? filter,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error
    )
    {
        filter = null;
        if (text is null)
        {
            error = "A filter is required.";
            return false;
        }

        var include = new List<FilterTerm>();
        var exclude = new List<FilterTerm>();
        var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var term in terms)
        {
            var excluding = term.StartsWith('-');
            var body = excluding ? term[1..] : term;
            if (!FilterTerm.TryParse(body, out var parsed, out var reason))
            {
                error = $"'{term}' is not a valid filter: {reason}";
                return false;
            }

            (excluding ? exclude : include).Add(parsed);
        }

        filter = new AprsFilter([.. include], [.. exclude], string.Join(' ', terms));
        error = null;
        return true;
    }

    /// <summary>
    /// Why an APRS-IS server would not accept this filter as it is, or <see langword="null"/> when it would: more than
    /// <see cref="ServerTermLimit"/> range or friend-range terms. Local evaluation has no such limit.
    /// </summary>
    public string? CheckServerLimits()
    {
        var terms = _text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.TrimStart('-')).ToArray();
        var ranges = terms.Count(t => t.StartsWith("r/", StringComparison.Ordinal));
        var friends = terms.Count(t => t.StartsWith("f/", StringComparison.Ordinal));
        return ranges > ServerTermLimit
                ? $"APRS-IS servers accept at most {ServerTermLimit} range (r/) terms; this filter has {ranges}."
            : friends > ServerTermLimit
                ? $"APRS-IS servers accept at most {ServerTermLimit} friend (f/) terms; this filter has {friends}."
            : null;
    }

    /// <summary>Whether the filter has no terms, so it matches nothing.</summary>
    public bool IsEmpty => _include.Length == 0 && _exclude.Length == 0;

    /// <summary>
    /// Whether <paramref name="packet"/> passes the filter. <paramref name="context"/> supplies station positions for
    /// the terms that need them.
    /// </summary>
    public bool Matches(AprsPacket packet, AprsFilterContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var facts = new PacketFacts(packet);
        return AnyMatches(_include, facts, context) && !AnyMatches(_exclude, facts, context);
    }

    // A loop rather than Any(), which would allocate a closure for every packet.
    private static bool AnyMatches(FilterTerm[] terms, PacketFacts facts, AprsFilterContext? context)
    {
        foreach (var term in terms)
        {
            if (term.Matches(facts, context))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The filter's terms, separated by single spaces.</summary>
    public override string ToString() => _text;

    internal static bool TryParseNumber(string text, double min, double max, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value)
        && value >= min
        && value <= max;
}
