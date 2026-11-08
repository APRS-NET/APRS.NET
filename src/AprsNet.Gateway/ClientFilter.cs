using System.Diagnostics.CodeAnalysis;
using AprsNet.Filters;

namespace AprsNet.Gateway;

/// <summary>
/// Parses the filter a client asks for, within the limits the server keeps so one client's filter cannot slow the
/// feed for everyone: every packet is checked against every client's filter.
/// </summary>
internal static class ClientFilter
{
    /// <summary>The most terms one filter may have. Real filters have a handful.</summary>
    public const int MaxTerms = 50;

    /// <summary>
    /// The most characters of callsign and name patterns (<c>p/</c>, <c>b/</c>, <c>o/</c>, <c>d/</c>, <c>e/</c>,
    /// <c>g/</c> and <c>u/</c>) one filter may have, about as many as fit in an APRS-IS login line.
    /// </summary>
    public const int MaxPatternCharacters = 512;

    /// <summary>
    /// Parses <paramref name="text"/> as <see cref="AprsFilter.TryParse"/> does, and refuses a filter over the limits:
    /// more than <see cref="AprsFilter.ServerTermLimit"/> <c>r/</c> or <c>f/</c> terms, as on APRS-IS, more than
    /// <see cref="MaxTerms"/> terms, or more than <see cref="MaxPatternCharacters"/> pattern characters.
    /// </summary>
    public static bool TryParse(
        string text,
        [NotNullWhen(true)] out AprsFilter? filter,
        [NotNullWhen(false)] out string? error
    )
    {
        if (!AprsFilter.TryParse(text, out filter, out error))
        {
            return false;
        }

        error = filter.CheckServerLimits() ?? CheckSize(filter.ToString());
        if (error is not null)
        {
            filter = null;
            return false;
        }

        return true;
    }

    private static string? CheckSize(string text)
    {
        var terms = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > MaxTerms)
        {
            return $"This server accepts at most {MaxTerms} terms in a filter; this filter has {terms.Length}.";
        }

        var patterns = 0;
        foreach (var term in terms)
        {
            var body = term.TrimStart('-');
            if (body.Length > 2 && body[1] == '/' && "pbodegu".Contains(body[0], StringComparison.Ordinal))
            {
                // The arguments, without the letter before them or the slashes between them.
                patterns += body.Length - 1 - body.Count(c => c == '/');
            }
        }

        return patterns > MaxPatternCharacters
            ? $"This server accepts at most {MaxPatternCharacters} characters of callsign and name patterns in a "
                + $"filter; this filter has {patterns}."
            : null;
    }
}
