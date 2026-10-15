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
        return _include.Any(term => term.Matches(facts, context))
            && !_exclude.Any(term => term.Matches(facts, context));
    }

    /// <summary>The filter's terms, separated by single spaces.</summary>
    public override string ToString() => _text;

    internal static bool TryParseNumber(string text, double min, double max, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value)
        && value >= min
        && value <= max;
}
