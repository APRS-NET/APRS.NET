using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace AprsNet.Packets;

/// <summary>Where a symbol taken from a packet's address came from: see <see cref="AddressSymbols"/>.</summary>
public enum AddressSymbolSource
{
    /// <summary>The destination: <c>GPSxyz</c>, <c>SPCxyz</c>, <c>SYMxyz</c>, <c>GPSCnn</c> or <c>GPSEnn</c>.</summary>
    Destination,

    /// <summary>The source's SSID, such as <c>-9</c> for a car.</summary>
    SourceSsid,
}

/// <summary>
/// The symbol a station that can't put one in its packets gets from its address (APRS101 pp. 92–93), such as a tracker
/// sending raw NMEA. The destination comes first; the source's SSID only when the destination gives none.
/// </summary>
/// <remarks>
/// <para>
/// <c>GPSxyz</c>, <c>SPCxyz</c> and <c>SYMxyz</c> name the symbol with two characters, the same codes as APRS101
/// Appendix 2 (<c>GPSMV</c> is the car); an optional sixth character is an overlay on an alternate-table symbol
/// (<c>GPSNV3</c> is a car with a 3). <c>GPSCnn</c> and <c>GPSEnn</c> name it by number, 01 to 94, on the primary or
/// alternate table (<c>GPSC12</c> is <c>/,</c>, the Boy Scouts).
/// </para>
/// <para>
/// APRS 1.1 calls the SSID symbols obsolete: today an SSID is only an informal hint at the kind of station. They're kept
/// for older trackers that rely on them.
/// </para>
/// </remarks>
public static class AddressSymbols
{
    // Each range of symbol codes and the two-character destination code of its first one: the first character is fixed
    // and the second counts up (APRS101 Appendix 2).
    private static readonly (char First, char Last, char Lead, char Start)[] _primary =
    [
        ('!', '/', 'B', 'B'),
        ('0', '9', 'P', '0'),
        (':', '@', 'M', 'R'),
        ('A', 'Z', 'P', 'A'),
        ('[', '`', 'H', 'S'),
        ('a', 'z', 'L', 'A'),
        ('{', '~', 'J', '1'),
    ];

    private static readonly (char First, char Last, char Lead, char Start)[] _alternate =
    [
        ('!', '/', 'O', 'B'),
        ('0', '9', 'A', '0'),
        (':', '@', 'N', 'R'),
        ('A', 'Z', 'A', 'A'),
        ('[', '`', 'D', 'S'),
        ('a', 'z', 'S', 'A'),
        ('{', '~', 'Q', '1'),
    ];

    // APRS101 p. 93: SSIDs 1 to 15.
    // Ambulance, bus, fire truck, bicycle, yacht, helicopter, small aircraft, ship, car, motorcycle, balloon, jeep, RV,
    // truck, van.
    private const string SsidCodes = "aUfbYX's><OjRkv";

    /// <summary>
    /// The symbol a packet without one gets from its address: from <paramref name="destination"/> if it names one,
    /// otherwise from <paramref name="source"/>'s SSID.
    /// </summary>
    /// <returns><see langword="false"/> when neither gives a symbol.</returns>
    public static bool TryFromAddress(
        Callsign source,
        Callsign destination,
        out AprsSymbol symbol,
        out AddressSymbolSource from
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (TryFromDestination(destination.Base, out symbol))
        {
            from = AddressSymbolSource.Destination;
            return true;
        }

        from = AddressSymbolSource.SourceSsid;
        return TryFromSsid(source.Ssid, out symbol);
    }

    /// <summary>The symbol a <c>GPSxyz</c>, <c>SPCxyz</c>, <c>SYMxyz</c>, <c>GPSCnn</c> or <c>GPSEnn</c> destination names.</summary>
    /// <param name="destination">The destination, without an SSID.</param>
    /// <param name="symbol">The symbol, with its overlay as the table when there is one.</param>
    /// <returns><see langword="false"/> when the destination names no symbol.</returns>
    public static bool TryFromDestination([NotNullWhen(true)] string? destination, out AprsSymbol symbol)
    {
        symbol = default;
        if (destination is not { Length: 5 or 6 })
        {
            return false;
        }

        var prefix = destination[..3].ToUpperInvariant();
        if (prefix is not ("GPS" or "SPC" or "SYM"))
        {
            return false;
        }

        var rest = destination[3..].ToUpperInvariant();
        if (
            prefix == "GPS"
            && rest.Length == 3
            && rest[0] is 'C' or 'E'
            && int.TryParse(rest.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number is >= 1 and <= 94
        )
        {
            return AprsSymbol.TryCreate(rest[0] == 'C' ? '/' : '\\', (char)(' ' + number), out symbol);
        }

        foreach (var (table, ranges) in new[] { ('/', _primary), ('\\', _alternate) })
        {
            foreach (var (first, last, lead, start) in ranges)
            {
                var offset = rest[1] - start;
                if (rest[0] != lead || offset < 0 || offset > last - first)
                {
                    continue;
                }

                var code = (char)(first + offset);
                // An overlay goes on an alternate-table symbol only, and takes the table's place.
                var overlay = rest.Length == 3 && table == '\\' ? rest[2] : table;
                return AprsSymbol.TryCreate(overlay, code, out symbol) || AprsSymbol.TryCreate(table, code, out symbol);
            }
        }

        return false;
    }

    /// <summary>The symbol APRS101 gives a source SSID, 1 to 15, such as <c>/&gt;</c>, the car, for 9.</summary>
    /// <param name="ssid">The SSID, as <see cref="Callsign.Ssid"/> gives it.</param>
    /// <param name="symbol">The symbol.</param>
    /// <returns><see langword="false"/> for no SSID, 0, or one that isn't a number from 1 to 15.</returns>
    public static bool TryFromSsid([NotNullWhen(true)] string? ssid, out AprsSymbol symbol)
    {
        symbol = default;
        if (
            !int.TryParse(ssid, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number is < 1 or > 15
            || ssid.StartsWith('0')
        )
        {
            return false;
        }

        symbol = new AprsSymbol('/', SsidCodes[number - 1]);
        return true;
    }
}
