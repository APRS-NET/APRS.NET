using System.Diagnostics.CodeAnalysis;
using AprsNet.Packets;

namespace AprsNet.Filters;

/// <summary>One term of a filter, such as <c>r/38.98/-94.67/50</c>.</summary>
internal abstract class FilterTerm
{
    public abstract bool Matches(PacketFacts packet, AprsFilterContext? context);

    public static bool TryParse(
        string text,
        [NotNullWhen(true)] out FilterTerm? term,
        [NotNullWhen(false)] out string? reason
    )
    {
        term = null;
        var parts = text.Split('/');
        var kind = parts[0];
        var args = parts[1..];
        // Only the symbol filter has lists that may be empty: s//# picks from the alternate table alone.
        if (args.Length == 0 || (kind != "s" && args.Any(arg => arg.Length == 0)))
        {
            reason = args.Length == 0 ? "it has no '/' arguments." : "it has an empty argument.";
            return false;
        }

        reason = kind switch
        {
            "r" => RangeTerm.TryParse(args, out term),
            "a" => AreaTerm.TryParse(args, out term),
            "p" => Make(new PrefixTerm(args), out term),
            "b" => Make(new PatternTerm(args, packet => packet.Packet.Source?.Value), out term),
            "o" => Make(new PatternTerm(args, packet => packet.ObjectName), out term),
            "d" => Make(new DigipeaterTerm(args), out term),
            "e" => Make(new PatternTerm(args, packet => packet.EntryStation), out term),
            "g" => Make(
                new PatternTerm(args, packet => (packet.Packet.Payload as MessagePayload)?.Addressee),
                out term
            ),
            "u" => Make(new PatternTerm(args, packet => packet.Packet.Destination?.Value), out term),
            "t" => TypeTerm.TryParse(args, out term),
            "s" => SymbolTerm.TryParse(args, out term),
            "q" => QConstructTerm.TryParse(args, out term),
            "m" => MyRangeTerm.TryParse(args, out term),
            "f" => FriendRangeTerm.TryParse(args, out term),
            _ => $"'{kind}' is not a filter type.",
        };
        return reason is null;
    }

    private static string? Make(FilterTerm made, out FilterTerm? term)
    {
        term = made;
        return null;
    }

    private static string? TryParseDistance(string text, out double km) =>
        AprsFilter.TryParseNumber(text, 0, 20_038, out km) ? null : $"'{text}' is not a distance in km.";

    private static bool Within(PacketFacts packet, AprsPosition? centre, double km) =>
        packet.Positioned is { } positioned
        && centre is { } from
        && Geodesy.DistanceKm(from, positioned.Position) <= km;

    /// <summary>Whether any pattern matches <paramref name="value"/>; a loop, as this runs for every packet.</summary>
    private static bool AnyGlob(string[] patterns, string value)
    {
        foreach (var pattern in patterns)
        {
            if (Glob(pattern, value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Case-insensitive match where <c>*</c> stands for any run of characters, including none.</summary>
    internal static bool Glob(string pattern, string value)
    {
        int p = 0,
            v = 0,
            star = -1,
            resume = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = v;
            }
            else if (p < pattern.Length && char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(value[v]))
            {
                p++;
                v++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                v = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    /// <summary><c>r/lat/lon/km</c>: positions within a distance of a point.</summary>
    private sealed class RangeTerm(AprsPosition centre, double km) : FilterTerm
    {
        public static string? TryParse(string[] args, out FilterTerm? term)
        {
            term = null;
            if (args.Length != 3)
            {
                return "a range is r/lat/lon/km.";
            }

            if (!AprsFilter.TryParseNumber(args[0], -90, 90, out var lat))
            {
                return $"'{args[0]}' is not a latitude.";
            }

            if (!AprsFilter.TryParseNumber(args[1], -180, 180, out var lon))
            {
                return $"'{args[1]}' is not a longitude.";
            }

            var error = TryParseDistance(args[2], out var km);
            term = error is null ? new RangeTerm(new AprsPosition(lat, lon), km) : null;
            return error;
        }

        public override bool Matches(PacketFacts packet, AprsFilterContext? context) => Within(packet, centre, km);
    }

    /// <summary><c>a/latN/lonW/latS/lonE</c>: positions inside a box.</summary>
    private sealed class AreaTerm(double north, double west, double south, double east) : FilterTerm
    {
        public static string? TryParse(string[] args, out FilterTerm? term)
        {
            term = null;
            if (args.Length != 4)
            {
                return "an area is a/latN/lonW/latS/lonE.";
            }

            if (
                !AprsFilter.TryParseNumber(args[0], -90, 90, out var north)
                || !AprsFilter.TryParseNumber(args[2], -90, 90, out var south)
            )
            {
                return "its latitudes must be numbers from -90 to 90.";
            }

            if (
                !AprsFilter.TryParseNumber(args[1], -180, 180, out var west)
                || !AprsFilter.TryParseNumber(args[3], -180, 180, out var east)
            )
            {
                return "its longitudes must be numbers from -180 to 180.";
            }

            if (north < south || east < west)
            {
                return "the north-west corner must be north and west of the south-east one.";
            }

            term = new AreaTerm(north, west, south, east);
            return null;
        }

        public override bool Matches(PacketFacts packet, AprsFilterContext? context) =>
            packet.Positioned?.Position is { } p
            && p.Latitude <= north
            && p.Latitude >= south
            && p.Longitude >= west
            && p.Longitude <= east;
    }

    /// <summary><c>p/aa/bb</c>: source callsigns starting with any of the prefixes.</summary>
    private sealed class PrefixTerm(string[] prefixes) : FilterTerm
    {
        public override bool Matches(PacketFacts packet, AprsFilterContext? context)
        {
            if (packet.Packet.Source?.Value is not { } source)
            {
                return false;
            }

            foreach (var prefix in prefixes)
            {
                if (source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary><c>b/</c>, <c>o/</c>, <c>e/</c>, <c>g/</c> and <c>u/</c>: one value of the packet against patterns.</summary>
    private sealed class PatternTerm(string[] patterns, Func<PacketFacts, string?> select) : FilterTerm
    {
        public override bool Matches(PacketFacts packet, AprsFilterContext? context) =>
            select(packet) is { } value && AnyGlob(patterns, value);
    }

    /// <summary><c>d/digi1/digi2</c>: packets repeated by any of the digipeaters.</summary>
    private sealed class DigipeaterTerm(string[] patterns) : FilterTerm
    {
        public override bool Matches(PacketFacts packet, AprsFilterContext? context)
        {
            var path = packet.Packet.Path;
            for (var i = 0; i < packet.DigipeaterCount; i++)
            {
                if (AnyGlob(patterns, path![i].Station.Value))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary><c>t/poimqstunw</c> and <c>t/poimqstunw/call/km</c>: packets of the given types.</summary>
    private sealed class TypeTerm(string types, string? call, double km) : FilterTerm
    {
        private const string Letters = "poimqstunw";

        public static string? TryParse(string[] args, out FilterTerm? term)
        {
            term = null;
            if (args.Length is not (1 or 3))
            {
                return "a type filter is t/types or t/types/call/km.";
            }

            var bad = args[0].FirstOrDefault(c => !Letters.Contains(c, StringComparison.Ordinal));
            if (bad != default)
            {
                return $"'{bad}' is not a packet type; use the letters {Letters}.";
            }

            if (args.Length == 1)
            {
                term = new TypeTerm(args[0], null, 0);
                return null;
            }

            var error = TryParseDistance(args[2], out var km);
            term = error is null ? new TypeTerm(args[0], args[1], km) : null;
            return error;
        }

        public override bool Matches(PacketFacts packet, AprsFilterContext? context)
        {
            foreach (var letter in types)
            {
                if (IsType(packet.Packet, letter))
                {
                    return call is null || Within(packet, context?.LastPosition?.Invoke(call), km);
                }
            }

            return false;
        }

        private static bool IsType(AprsPacket packet, char letter) =>
            letter switch
            {
                'p' => packet.Payload is PositionReport or MicEReport,
                'o' => packet.Payload is ObjectReport,
                'i' => packet.Payload is ItemReport,
                // As on APRS-IS: every message, NWS bulletins included, and telemetry definitions, which are messages.
                'm' => packet.Payload is MessagePayload or TelemetryDefinition,
                'q' => packet.Payload is MessagePayload { Kind: MessageKind.Query } || IsGeneralQuery(packet),
                's' => packet.Payload is StatusReport,
                't' => packet.Payload is TelemetryReport or TelemetryDefinition,
                'u' => packet.Payload is UserDefinedPayload,
                'n' => packet.Payload is MessagePayload { Kind: MessageKind.NwsBulletin } || IsNwsUnderscore(packet),
                'w' => packet.Payload is WeatherReport or RawWeatherReport or PositionedPayload { Weather: not null },
                _ => false,
            };

        // APRS-IS servers count NWS_ addressees (the compressed bulletin format) as NWS too.
        private static bool IsNwsUnderscore(AprsPacket packet) =>
            packet.Payload is MessagePayload message && message.Addressee.StartsWith("NWS_", StringComparison.Ordinal);

        // A '?' query (?APRS?, ?IGATE?) is not decoded yet, so it has no payload.
        private static bool IsGeneralQuery(AprsPacket packet) =>
            packet.Payload is null && packet.Information.Span is [(byte)'?', ..];
    }

    /// <summary><c>s/pri/alt/over</c>: symbols from the primary table, the alternate table, and overlays.</summary>
    private sealed class SymbolTerm(string primary, string alternate, string overlays) : FilterTerm
    {
        public static string? TryParse(string[] args, out FilterTerm? term)
        {
            term = null;
            if (args.Length > 3 || args.All(arg => arg.Length == 0))
            {
                return "a symbol filter is s/pri, s/pri/alt or s/pri/alt/over, with at least one symbol.";
            }

            term = new SymbolTerm(args[0], args.ElementAtOrDefault(1) ?? "", args.ElementAtOrDefault(2) ?? "");
            return null;
        }

        public override bool Matches(PacketFacts packet, AprsFilterContext? context) =>
            packet.Positioned?.Symbol is { } symbol
            && (
                symbol.IsPrimary
                    ? primary.Contains(symbol.Code, StringComparison.Ordinal)
                    : alternate.Contains(symbol.Code, StringComparison.Ordinal)
                        && (
                            overlays.Length == 0
                            || symbol.Overlay is { } o && overlays.Contains(o, StringComparison.Ordinal)
                        )
            );
    }

    /// <summary><c>q/con</c>: packets that entered APRS-IS with one of the q-construct letters.</summary>
    private sealed class QConstructTerm(string letters) : FilterTerm
    {
        public static string? TryParse(string[] args, out FilterTerm? term)
        {
            term = null;
            if (args.Length != 1)
            {
                // q/con/I also passes the positions of IGates it sees, which needs a station store.
                return "only q/con is supported, without the /I option.";
            }

            term = new QConstructTerm(args[0]);
            return null;
        }

        public override bool Matches(PacketFacts packet, AprsFilterContext? context) =>
            packet.QConstruct is { } letter && letters.Contains(letter, StringComparison.Ordinal);
    }

    /// <summary><c>m/km</c>: positions within a distance of the client's own position.</summary>
    private sealed class MyRangeTerm(double km) : FilterTerm
    {
        public static string? TryParse(string[] args, out FilterTerm? term)
        {
            term = null;
            if (args.Length != 1)
            {
                return "a my-range filter is m/km.";
            }

            var error = TryParseDistance(args[0], out var km);
            term = error is null ? new MyRangeTerm(km) : null;
            return error;
        }

        public override bool Matches(PacketFacts packet, AprsFilterContext? context) =>
            Within(packet, context?.MyPosition, km);
    }

    /// <summary><c>f/call/km</c>: positions within a distance of another station's last position.</summary>
    private sealed class FriendRangeTerm(string call, double km) : FilterTerm
    {
        public static string? TryParse(string[] args, out FilterTerm? term)
        {
            term = null;
            if (args.Length != 2)
            {
                return "a friend-range filter is f/call/km.";
            }

            var error = TryParseDistance(args[1], out var km);
            term = error is null ? new FriendRangeTerm(args[0], km) : null;
            return error;
        }

        public override bool Matches(PacketFacts packet, AprsFilterContext? context) =>
            Within(packet, context?.LastPosition?.Invoke(call), km);
    }
}
