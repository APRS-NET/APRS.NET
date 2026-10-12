#:property TargetFramework=net10.0
#:property TargetFrameworks=
#:property PublishAot=false

// Anonymizes aprscli capture files ("<UTC timestamp> <raw line>", Latin-1) for tests/corpus.
//
//   dotnet run tools/corpus/Anonymize.cs -- --map <mapping.json> --out <file> <capture>...
//
// Callsigns become same-length Q-prefixed calls (Q is not an ITU national prefix, so they can never be real),
// named stations become same-length X-names, and e-mail addresses are masked. Every substitution keeps the length,
// so each output line is exactly as long as its input line, and the tool checks that. Positions, device TOCALLs,
// Mic-E destinations, generic path aliases and APRS-IS server names are kept byte for byte.
//
// The mapping file is the key to undo the anonymization: keep it OUT of the repository. It is read if it exists and
// written back, so later captures reuse the same pseudonyms. Its "literals" section can hold extra same-length
// replacements (for example a person's name found in a comment) that should never be committed either.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var latin1 = Encoding.Latin1;
string? mapPath = null;
string? outPath = null;
var inputs = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--map":
            mapPath = args[++i];
            break;
        case "--out":
            outPath = args[++i];
            break;
        default:
            inputs.Add(args[i]);
            break;
    }
}

if (mapPath is null || outPath is null || inputs.Count == 0)
{
    Console.Error.WriteLine("usage: Anonymize.cs --map <mapping.json> --out <file> <capture>...");
    return 3;
}

var map = File.Exists(mapPath)
    ? JsonSerializer.Deserialize<Mapping>(File.ReadAllText(mapPath)) ?? new Mapping()
    : new Mapping();
var anonymizer = new Anonymizer(map);

var lines = inputs.SelectMany(path => File.ReadAllLines(path, latin1)).Where(l => l.Length > 0).ToList();

// Pass 1 learns every identifier so pass 2 can also replace mentions that appear before the station is first seen.
foreach (var line in lines)
{
    anonymizer.Learn(Anonymizer.SplitCapture(line).Packet);
}

var output = new List<string>(lines.Count);
foreach (var line in lines)
{
    var (stamp, packet) = Anonymizer.SplitCapture(line);
    var result = anonymizer.Rewrite(packet);
    if (result.Length != packet.Length)
    {
        Console.Error.WriteLine($"length changed, refusing to write: {packet}");
        return 1;
    }

    output.Add(stamp is null ? result : $"{stamp} {result}");
}

var leaks = anonymizer.FindLeaks(output);
if (leaks.Count > 0)
{
    Console.Error.WriteLine("original identifiers still present:");
    foreach (var leak in leaks)
    {
        Console.Error.WriteLine($"  {leak}");
    }

    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllText(outPath, string.Join('\n', output) + "\n", latin1);
File.WriteAllText(mapPath, JsonSerializer.Serialize(map, Mapping.JsonOptions));
Console.WriteLine(
    $"{output.Count} lines -> {outPath}; {map.Calls.Count} callsigns, {map.Names.Count} station names mapped"
);
return 0;

internal sealed class Mapping
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SortedDictionary<string, string> Calls { get; set; } = new(StringComparer.Ordinal);

    public SortedDictionary<string, string> Names { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Extra same-length replacements, applied to packet bodies. Edit by hand.</summary>
    public SortedDictionary<string, string> Literals { get; set; } = new(StringComparer.Ordinal);
}

internal sealed partial class Anonymizer(Mapping map)
{
    // Generic aliases and APRS-IS infrastructure that identify no person.
    private static readonly HashSet<string> _keep = new(StringComparer.OrdinalIgnoreCase)
    {
        "TCPIP",
        "TCPXX",
        "NOGATE",
        "RFONLY",
        "RELAY",
        "WIDE",
        "TRACE",
        "IGATECALL",
        "FIRST",
        "SECOND",
        "THIRD",
        "FOURTH",
        "FIFTH",
        "SIXTH",
        "WINLINK",
    };

    public static (string? Stamp, string Packet) SplitCapture(string line)
    {
        var space = line.IndexOf(' ', StringComparison.Ordinal);
        return space == 24 && line[4] == '-' && line[^1] != ' ' ? (line[..space], line[(space + 1)..]) : (null, line);
    }

    public void Learn(string packet)
    {
        var (header, body) = SplitPacket(packet);
        if (header is null)
        {
            return;
        }

        var source = header[..header.IndexOf('>', StringComparison.Ordinal)];
        LearnToken(source);
        var path = header[(source.Length + 1)..].Split(',');
        for (var i = 1; i < path.Length; i++) // path[0] is the TOCALL (or Mic-E data): never touched
        {
            LearnToken(path[i].TrimEnd('*'));
        }

        // Callsigns in object names, message addressees and comment text ("K0HAM" in a repeater object).
        foreach (var text in TextParts(body))
        {
            foreach (Match m in CallsignInText().Matches(EncodedToken().Replace(text, m => new string(' ', m.Length))))
            {
                if (!_keep.Contains(m.Value) && !IsGeneric(m.Value) && !NotACallsign().IsMatch(m.Value))
                {
                    LearnCall(m.Value);
                }
            }
        }
    }

    /// <summary>The parts of a body that are free text rather than encoded data.</summary>
    private static IEnumerable<string> TextParts(string body)
    {
        var keep = ProtectedPrefix(body);
        if (body.Length >= 10 && body[0] == ';')
        {
            yield return body[1..10];
        }

        yield return body[keep..];
    }

    public string Rewrite(string packet)
    {
        var (header, body) = SplitPacket(packet);
        if (header is null)
        {
            return ReplaceText(packet);
        }

        var gt = header.IndexOf('>', StringComparison.Ordinal);
        var path = header[(gt + 1)..].Split(',');
        for (var i = 1; i < path.Length; i++)
        {
            var star = path[i].EndsWith('*') ? "*" : "";
            path[i] = MapToken(path[i].TrimEnd('*')) + star;
        }

        var newHeader = MapToken(header[..gt]) + ">" + string.Join(',', path);
        return newHeader + ":" + RewriteBody(body);
    }

    public List<string> FindLeaks(IEnumerable<string> lines)
    {
        var originals = map
            .Calls.Keys.Select(BaseCall)
            .Concat(map.Names.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var leaks = new List<string>();
        foreach (var line in lines)
        {
            var packet = SplitCapture(line).Packet;
            var (header, _) = SplitPacket(packet);
            var tocallEnd = header is null ? 0 : IndexOfTocallEnd(header);
            foreach (var original in originals)
            {
                foreach (Match m in Word(original).Matches(packet))
                {
                    // Mic-E destinations and other TOCALLs are data, not identities.
                    var gt = packet.IndexOf('>', StringComparison.Ordinal);
                    if (m.Index > gt && m.Index < tocallEnd)
                    {
                        continue;
                    }

                    leaks.Add($"{original}: {packet}");
                }
            }
        }

        return leaks;
    }

    private static int IndexOfTocallEnd(string header)
    {
        var gt = header.IndexOf('>', StringComparison.Ordinal);
        var comma = header.IndexOf(',', gt);
        return comma < 0 ? header.Length : comma;
    }

    private static (string? Header, string Body) SplitPacket(string packet)
    {
        var colon = packet.IndexOf(':', StringComparison.Ordinal);
        var gt = packet.IndexOf('>', StringComparison.Ordinal);
        return colon > 0 && gt > 0 && gt < colon ? (packet[..colon], packet[(colon + 1)..]) : (null, packet);
    }

    /// <summary>Bytes at the start of the body that are encoded data (positions, Mic-E) and must not be rewritten.</summary>
    private static int ProtectedPrefix(string body)
    {
        if (body.Length == 0)
        {
            return 0;
        }

        int Position(int at) =>
            at < body.Length && char.IsAsciiDigit(body[at])
                ? at + 19 // uncompressed lat, table, lon, symbol
                : at + 13; // compressed

        var end = body[0] switch
        {
            '!' or '=' => Position(1),
            '/' or '@' => Position(8), // after the 7-byte timestamp
            ';' => Position(18), // name(9), live/killed flag, timestamp(7); RewriteBody handles the name
            '`' or '\'' => 9, // Mic-E longitude, speed/course, symbol, table
            '_' => 9, // positionless weather timestamp
            _ => 0,
        };
        return Math.Min(end, body.Length);
    }

    private string RewriteBody(string body)
    {
        var keep = ProtectedPrefix(body);
        if (body.Length >= 10 && body[0] == ';')
        {
            // The object name sits inside the protected prefix but can hold a callsign (P-NZ0P, KC0QBU-13).
            return ";" + ReplaceText(body[1..10]) + body[10..keep] + ReplaceText(body[keep..]);
        }

        return body[..keep] + ReplaceText(body[keep..]);
    }

    /// <summary>Rewrites free text, leaving !DAO! and |base-91 telemetry| tokens byte for byte.</summary>
    private string ReplaceText(string text)
    {
        var result = new StringBuilder(text.Length);
        var at = 0;
        foreach (Match token in EncodedToken().Matches(text))
        {
            result.Append(ReplacePlain(text[at..token.Index])).Append(token.Value);
            at = token.Index + token.Length;
        }

        return result.Append(ReplacePlain(text[at..])).ToString();
    }

    private string ReplacePlain(string text)
    {
        text = Email().Replace(text, m => Mask(m.Value));
        foreach (var (original, replacement) in map.Literals)
        {
            text = text.Replace(original, replacement, StringComparison.Ordinal);
        }

        foreach (var (original, replacement) in map.Names)
        {
            text = Word(original).Replace(text, m => MatchCase(m.Value, replacement));
        }

        return CallsignInText()
            .Replace(
                text,
                m =>
                    map.Calls.TryGetValue(m.Value.ToUpperInvariant(), out var mapped)
                        ? MatchCase(m.Value, mapped)
                        : m.Value
            );
    }

    private string MapToken(string token)
    {
        if (token.Length == 0 || _keep.Contains(token) || IsGeneric(token))
        {
            return token;
        }

        var dash = token.IndexOf('-', StringComparison.Ordinal);
        var call = dash < 0 ? token : token[..dash];
        var ssid = dash < 0 ? "" : token[dash..];
        if (map.Calls.TryGetValue(call.ToUpperInvariant(), out var mappedCall))
        {
            return MatchCase(call, mappedCall) + ssid;
        }

        return map.Names.TryGetValue(token.ToUpperInvariant(), out var mappedName)
            ? MatchCase(token, mappedName)
            : token;
    }

    private void LearnToken(string token)
    {
        if (token.Length == 0 || _keep.Contains(token) || IsGeneric(token))
        {
            return;
        }

        var call = token.Split('-')[0];
        if (CallsignExact().IsMatch(call))
        {
            LearnCall(call);
        }
        else if (!map.Names.ContainsKey(token.ToUpperInvariant()))
        {
            map.Names[token.ToUpperInvariant()] = NewName(token.Length);
        }
    }

    private void LearnCall(string callWithSsid)
    {
        var call = BaseCall(callWithSsid).ToUpperInvariant();
        if (!map.Calls.ContainsKey(call))
        {
            map.Calls[call] = NewCall(call);
        }
    }

    private static string BaseCall(string call) => call.Split('-')[0];

    private static bool IsGeneric(string token) => Generic().IsMatch(token);

    private string NewCall(string call)
    {
        // Keep digits in place; the first letter becomes Q and the other letters count up.
        var taken = new HashSet<string>(map.Calls.Values, StringComparer.Ordinal);
        for (var n = 0; ; n++)
        {
            var chars = call.ToCharArray();
            var letters = Enumerable.Range(0, chars.Length).Where(i => char.IsAsciiLetter(chars[i])).ToList();
            chars[letters[0]] = 'Q';
            var counter = n;
            for (var k = letters.Count - 1; k >= 1; k--)
            {
                chars[letters[k]] = (char)('A' + (counter % 26));
                counter /= 26;
            }

            var candidate = new string(chars);
            if (counter == 0 && taken.Add(candidate))
            {
                return candidate;
            }

            if (counter > 0)
            {
                throw new InvalidOperationException($"no free pseudonym of the shape of {call}");
            }
        }
    }

    private string NewName(int length)
    {
        var taken = new HashSet<string>(map.Names.Values, StringComparer.Ordinal);
        for (var n = 0; ; n++)
        {
            var chars = new char[length];
            chars[0] = 'X';
            var counter = n;
            for (var k = length - 1; k >= 1; k--)
            {
                chars[k] = (char)('A' + (counter % 26));
                counter /= 26;
            }

            var candidate = new string(chars);
            if (counter == 0 && taken.Add(candidate))
            {
                return candidate;
            }

            if (counter > 0)
            {
                throw new InvalidOperationException($"no free station name of length {length}");
            }
        }
    }

    private static string MatchCase(string original, string replacement)
    {
        var chars = replacement.ToCharArray();
        for (var i = 0; i < chars.Length && i < original.Length; i++)
        {
            if (char.IsAsciiLetterLower(original[i]))
            {
                chars[i] = char.ToLowerInvariant(chars[i]);
            }
        }

        return new string(chars);
    }

    /// <summary>Same-length mask: letters become x, digits 0; @ and . stay.</summary>
    private static string Mask(string email) =>
        string.Create(
            email.Length,
            email,
            (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    span[i] =
                        char.IsAsciiLetter(source[i]) ? 'x'
                        : char.IsAsciiDigit(source[i]) ? '0'
                        : source[i];
                }
            }
        );

    private static Regex Word(string text) =>
        new(
            $@"(?<![A-Za-z0-9]){Regex.Escape(text)}(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

    // An amateur callsign: prefix (1-2 letters, or digit+letter, or letter+digit), one digit, 1-4 letters.
    [GeneratedRegex(@"^(?:[A-Z]{1,2}|[0-9][A-Z]|[A-Z][0-9])[0-9][A-Z]{1,4}$", RegexOptions.IgnoreCase)]
    private static partial Regex CallsignExact();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?:[A-Z]{1,2}|[0-9][A-Z]|[A-Z][0-9])[0-9][A-Z]{1,4}(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex CallsignInText();

    [GeneratedRegex(
        @"^(?:WIDE[1-7](?:-[1-7])?|TRACE[1-7]?(?:-[1-7])?|TEMP[1-7]-[1-7]|qA[A-Za-z]|T2[A-Z0-9]+|[A-Z]{2}[1-7]-[1-7]|AMBCWOP-\d+|WLNK-\d+|CWOP-\d+|CORE-\d+)$"
    )]
    private static partial Regex Generic();

    // Callsign-shaped words that are APRS field values: AFRS range (R25m, E10k) and digital voice modes.
    [GeneratedRegex(@"^(?:[RNSEW][0-9]{2}[MK]|C4FM)$", RegexOptions.IgnoreCase)]
    private static partial Regex NotACallsign();

    // !DAO! precision (datum + two bytes) and |base-91 comment telemetry|: encoded data, never text.
    [GeneratedRegex(@"![\x21-\x7b]{3}!|\|(?:[\x21-\x7b]{2}){2,7}\|")]
    private static partial Regex EncodedToken();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+")]
    private static partial Regex Email();
}
