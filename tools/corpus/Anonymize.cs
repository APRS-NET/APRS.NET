#:property TargetFramework=net10.0
#:property TargetFrameworks=
#:property PublishAot=false

// Anonymizes aprscli capture files ("<UTC timestamp> <raw line>", Latin-1) for tests/corpus.
//
//   dotnet run tools/corpus/Anonymize.cs -- --map <mapping.json> --out <file> <capture>...
//
// Callsigns become same-length Q-prefixed calls (Q is not an ITU national prefix, so they can never be real; digit-first
// calls such as 7K1ABC become 0Q1AAA, since no prefix starts with 0),
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
var dropBinary = false;
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
        case "--drop-binary":
            dropBinary = true;
            break;
        default:
            inputs.Add(args[i]);
            break;
    }
}

if (mapPath is null || outPath is null || inputs.Count == 0)
{
    Console.Error.WriteLine("usage: Anonymize.cs --map <mapping.json> --out <file> [--drop-binary] <capture>...");
    return 3;
}

var map = File.Exists(mapPath)
    ? JsonSerializer.Deserialize<Mapping>(File.ReadAllText(mapPath)) ?? new Mapping()
    : new Mapping();
var anonymizer = new Anonymizer(map);

var lines = inputs.SelectMany(path => File.ReadAllLines(path, latin1)).Where(l => l.Length > 0).ToList();

// A broken gateway can leak KISS frames into a packet: binary AX.25 headers hold callsigns as shifted bytes, which
// the text rules cannot see or rewrite. Refuse them, or drop them with --drop-binary.
var binary = lines.Where(Anonymizer.HasKissFrame).ToList();
if (binary.Count > 0)
{
    if (!dropBinary)
    {
        Console.Error.WriteLine(
            $"{binary.Count} lines carry KISS frames whose binary AX.25 headers cannot be anonymized; "
                + "rerun with --drop-binary to leave them out:"
        );
        binary.ForEach(l => Console.Error.WriteLine($"  {l}"));
        return 1;
    }

    lines = [.. lines.Except(binary)];
    Console.WriteLine($"dropped {binary.Count} lines carrying KISS frames");
}

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
        "LOCAL", // a generic path alias in the Czech network (APNU19,LOCAL,qAR), and a bulletin group (BLN1LOCAL)
        "TRACE",
        "IGATECALL",
        "FIRST",
        "SECOND",
        "THIRD",
        "FOURTH",
        "FIFTH",
        "SIXTH",
        "SEVENTH",
        "EIGHTH",
        "NINTH",
        "TENTH",
        "APRSFI", // aprs.fi's APRS-IS login (APRSFI-I3)
        "APRS", // the generic destination, and South Africa's APRS-IS server APRS-ZA
        "WINLINK",
        "QTH", // "location": one DMR hotspot puts it in the path
        "DMR", // digital voice modes, which gateways put in the path
        "DSTAR",
        "YSF",
        "NXDN",
        "C4FM",
        "DEMO", // APRS-IS simulators (DEMO-002, MARSQ-052): not people, and their 3-digit SSIDs are worth keeping
        "MARSQ",
        "ECHO", // a generic APRS101 alias
        "IGATE", // generic words that some stations use as path elements; as X-names they would rewrite comments
        "DIRECT",
        "SOUTH",
        "SUNSET",
        "CHURCH",
        "UTC",
        "ARES",
        "MARS",
        "NASA",
        "KLUB",
        "SOTA",
        "OTA",
        "ORG",
        "GRP",
        "OPR",
        "ROLL",
        "TGIF",
        "MY",
        "NWS", // NWS-xxxxx bulletin addressees and the services that send them
        "WXALRT",
        "WXMY", // a Malaysian weather service: its stations (WXMY-5) and BLNnWXMY bulletins identify no one
        "WHO", // APRS message services and bots, which identify no one
        "WHOIS",
        "JOKE",
        "REPEAT",
        "SMS",
        "MAIL",
        "IRC",
        "MPAD",
        "WXBOT",
        "WXNOW",
        "WXYO",
        "ANSRVR",
        "CQSRVR",
        "RGSTRY",
        "HAMAPI",
        "EHAM",
        "QRX",
    };

    /// <summary>
    /// Whether a line holds a KISS frame delimiter (0xC0, FEND), raw or after a UTF-8 re-encoding of the bytes
    /// (C3 80), which is how leaked binary frames usually arrive on APRS-IS.
    /// </summary>
    public static bool HasKissFrame(string line) =>
        line.Contains('\u00C0', StringComparison.Ordinal) || line.Contains("\u00C3\u0080", StringComparison.Ordinal);

    public static (string? Stamp, string Packet) SplitCapture(string line)
    {
        var space = line.IndexOf(' ', StringComparison.Ordinal);
        return space == 24 && line[4] == '-' && line.Length > space + 1
            ? (line[..space], line[(space + 1)..])
            : (null, line);
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

    private static bool IsTelemetryDefinition(string body) => TelemetryDefinition().IsMatch(body);

    /// <summary>The parts of a body that are free text rather than encoded data.</summary>
    private static IEnumerable<string> TextParts(string body)
    {
        var keep = ProtectedPrefix(body);
        if (body.Length >= 10 && body[0] is ';' or ':')
        {
            yield return body[1..10];
        }

        if (body.Length > 0 && body[0] == ')' && ItemNameEnd(body) is var name and > 0)
        {
            yield return body[1..name];
        }

        if (!IsTelemetryDefinition(body))
        {
            yield return body[keep..];
        }
        else if (
            body[10..].StartsWith(":BITS.", StringComparison.Ordinal) && body.IndexOf(',', 10) is var comma and > 0
        )
        {
            yield return body[(comma + 1)..];
        }
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
                foreach (Match m in LeakPattern(original).Matches(packet))
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
            ')' when ItemNameEnd(body) is var name and > 0 => Position(name + 1), // name(3-9), live/killed flag
            '`' or '\'' => 9, // Mic-E longitude, speed/course, symbol, table
            '_' => 9, // positionless weather timestamp
            _ => 0,
        };
        return Math.Min(end, body.Length);
    }

    /// <summary>Where an item's 3–9 character name ends: the index of its <c>!</c> (live) or <c>_</c> (killed) flag.</summary>
    private static int ItemNameEnd(string body)
    {
        var flag = body.AsSpan(4, Math.Min(7, Math.Max(0, body.Length - 4))).IndexOfAny('!', '_');
        return flag < 0 ? 0 : flag + 4;
    }

    private string RewriteBody(string body)
    {
        var keep = ProtectedPrefix(body);
        if (body.Length >= 10 && body[0] == ';')
        {
            // The object name sits inside the protected prefix but can hold a callsign (P-NZ0P, KC0QBU-13).
            return ";" + ReplaceText(body[1..10]) + body[10..keep] + ReplaceText(body[keep..]);
        }

        if (body.Length > 0 && body[0] == ')' && ItemNameEnd(body) is var item and > 0)
        {
            // Likewise an item name (RPTR-1, or a callsign); the position after it is encoded data.
            return ")" + ReplaceText(body[1..item]) + body[item..keep] + ReplaceText(body[keep..]);
        }

        if (IsTelemetryDefinition(body))
        {
            // PARM./UNIT./EQNS./BITS. text is channel names and units ("Rx1h" looks like a callsign), so only the
            // addressee is anonymized, and the BITS. project name, which is free text that can name the station.
            var definition = body[10..];
            var project = definition.StartsWith(":BITS.", StringComparison.Ordinal) ? definition.IndexOf(',') : -1;
            return ":"
                + ReplaceText(body[1..10])
                + (project < 0 ? definition : definition[..(project + 1)] + ReplaceText(definition[(project + 1)..]));
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
        var (tag, station) = SplitTag(token);
        if (tag.Length > 0)
        {
            return tag + MapToken(station);
        }

        var dash = token.IndexOf('-', StringComparison.Ordinal);
        var call = dash < 0 ? token : token[..dash];
        var ssid = dash < 0 ? "" : token[dash..];
        if (token.Length == 0 || _keep.Contains(token) || _keep.Contains(call) || IsGeneric(token))
        {
            return token;
        }

        if (map.Calls.TryGetValue(call.ToUpperInvariant(), out var mappedCall))
        {
            return MatchCase(call, mappedCall) + ssid;
        }

        // Named stations keep their SSID too, so odd ones (DIGI-12, NODE-002) survive anonymization.
        return map.Names.TryGetValue(call.ToUpperInvariant(), out var mappedName)
            ? MatchCase(call, mappedName) + ssid
            : token;
    }

    private void LearnToken(string token)
    {
        token = SplitTag(token).Station;
        var call = token.Split('-')[0];
        // A one-letter path element (one LoRa firmware sends "Q") identifies nobody, and its X-name would be ambiguous.
        if (call.Length < 2 || _keep.Contains(token) || _keep.Contains(call) || IsGeneric(token))
        {
            return;
        }

        if (CallsignExact().IsMatch(call))
        {
            if (!NotACallsign().IsMatch(call))
            {
                LearnCall(call);
            }
        }
        else if (!map.Names.ContainsKey(call.ToUpperInvariant()))
        {
            map.Names[call.ToUpperInvariant()] = NewName(call.Length);
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

    /// <summary>Splits a tag that is not an identity off a station name: SvxLink calls its EchoLink nodes EL-call
    /// (link) and ER-call (repeater).</summary>
    private static (string Tag, string Station) SplitTag(string token) =>
        EchoLinkTag().Match(token) is { Success: true } m ? (m.Value, token[m.Length..]) : ("", token);

    private static bool IsGeneric(string token) => Generic().IsMatch(token);

    private string NewCall(string call)
    {
        // Keep digits in place; the first letter becomes Q and the other letters count up. A digit-first prefix
        // (7K, 2E) starts with 0 instead: no ITU prefix begins with 0, while some digit+Q prefixes are real (7Q).
        var taken = new HashSet<string>(map.Calls.Values, StringComparer.Ordinal);
        for (var n = 0; ; n++)
        {
            var chars = call.ToCharArray();
            var letters = Enumerable.Range(0, chars.Length).Where(i => char.IsAsciiLetter(chars[i])).ToList();
            chars[letters[0]] = 'Q';
            if (char.IsAsciiDigit(chars[0]))
            {
                chars[0] = '0';
            }

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

    // Stricter than Word for the final check: a digit right before the identifier still counts (glued comments).
    private static Regex LeakPattern(string text) =>
        new(
            $@"(?<![A-Za-z]){Regex.Escape(text)}(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
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

    [GeneratedRegex(@"^E[LR]-(?=(?:[A-Z]{1,2}|[0-9][A-Z]|[A-Z][0-9])[0-9][A-Z]{1,4}(?:-|$))", RegexOptions.IgnoreCase)]
    private static partial Regex EchoLinkTag();

    [GeneratedRegex(
        @"^(?:WIDE[1-7](?:-[1-7])?|TRACE[1-7]?(?:-[1-7])?|TEMP[1-7]-[1-7]|qA[A-Za-z]|T2[A-Z0-9]+|[A-Z]{2,5}[1-7]-[1-7]|AMBCWOP-\d+|WLNK-\d+|CWOP-\d+|CORE-\d+)$",
        RegexOptions.IgnoreCase // some stations send lower-case aliases (wide2-1)
    )]
    private static partial Regex Generic();

    // Callsign-shaped words that are APRS field values: AFRS range (R25m, E10k), digital voice modes, and weather
    // software tags after the weather data (APRS101 ch. 12: software d/M/P/S/W/X plus an Ultimeter unit, "XU2k"; one station sends "TU2K"), and APRX telemetry channel
    // names (Rx1h, Dg1h), which one user-defined packet quotes outside a PARM. message.
    [GeneratedRegex(
        @"^(?:[RNSEW][0-9]{2}[MK]|C4FM|[dMPSTWX]U2(?:k|kr|R)|(?:RX|TX|DG)[0-9]H)$",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex NotACallsign();

    [GeneratedRegex(@"^:.{9}:(?:PARM|UNIT|EQNS|BITS)\.")]
    private static partial Regex TelemetryDefinition();

    // !DAO! precision (datum + two bytes) and |base-91 comment telemetry|: encoded data, never text.
    // Also the fixed-width fields a comment can be glued to without a space ("/A=000040W6XYZ-10", "PHG4540K2XYZ",
    // weather "b10175VK2XYZ"), so a callsign right after them is still seen as a separate word.
    [GeneratedRegex(
        @"![\x21-\x7b]{3}!|\|(?:[\x21-\x7b]{2}){2,7}\||/A=-?[0-9]{5,6}|PHG[0-9][0-9:;<=>?@A-Z][0-9]{2}|RNG[0-9]{4}|DFS[0-9]{4}|(?<=[0-9.])(?:b[0-9]{5}|h[0-9]{2}|[Ll][0-9]{3})"
    )]
    private static partial Regex EncodedToken();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+")]
    private static partial Regex Email();
}
