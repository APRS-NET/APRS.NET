using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AprsNet.Packets.CorpusTests;

/// <summary>
/// Checks the anonymized captures in tests/corpus: the "&lt;UTC timestamp&gt; &lt;raw line&gt;" format, and that nothing
/// un-anonymized slipped in. Decoding tests against these packets will join this project once the parser exists.
/// </summary>
public partial class CorpusFileTests
{
    private static readonly string _directory = Path.Combine(AppContext.BaseDirectory, "corpus");

    // Generic aliases and APRS-IS infrastructure that the anonymizer keeps (tools/corpus/Anonymize.cs).
    private static readonly HashSet<string> _kept = new(StringComparer.OrdinalIgnoreCase)
    {
        "TCPIP",
        "TCPXX",
        "NOGATE",
        "RFONLY",
        "RELAY",
        "WIDE",
        "TRACE",
        "FIRST",
        "SECOND",
        "THIRD",
        "FOURTH",
        "FIFTH",
        "SIXTH",
        "WINLINK",
        "DMR",
        "DSTAR",
        "YSF",
        "NXDN",
        "C4FM",
    };

    public static TheoryData<string> CorpusFiles()
    {
        var data = new TheoryData<string>();
        foreach (
            var path in Directory
                .GetFiles(_directory, "*.log", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
        )
        {
            data.Add(Path.GetRelativePath(_directory, path).Replace('\\', '/'));
        }

        return data;
    }

    [Fact]
    public void TheCorpusIsNotEmpty()
    {
        Assert.NotEmpty(CorpusFiles());
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void EveryLineIsATimestampedPacketInTimeOrder(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(_directory, file));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);

        var previous = DateTimeOffset.MinValue;
        var lineNumber = 0;
        foreach (var line in Lines(file))
        {
            lineNumber++;
            var match = CaptureLine().Match(line);
            Assert.True(match.Success, $"{file}:{lineNumber} is not '<timestamp> SRC>DEST[,PATH]:INFO': {line}");

            var stamp = DateTimeOffset.ParseExact(
                match.Groups["stamp"].Value,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal
            );
            Assert.True(stamp >= previous, $"{file}:{lineNumber} goes back in time");
            previous = stamp;
        }

        Assert.True(lineNumber > 0, $"{file} is empty");
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void CallsignsAreAnonymized(string file)
    {
        var lineNumber = 0;
        foreach (var line in Lines(file))
        {
            lineNumber++;
            var match = CaptureLine().Match(line);
            var path = match.Groups["path"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var station in path.Prepend(match.Groups["source"].Value))
            {
                var call = station.TrimEnd('*').Split('-')[0];
                if (_kept.Contains(call) || Generic().IsMatch(station.TrimEnd('*')) || !Callsign().IsMatch(call))
                {
                    continue; // not a callsign (named stations become X-names, which have no digit)
                }

                // Pseudonyms start with Q, or with 0Q for digit-first prefixes (no ITU prefix starts with 0).
                Assert.True(
                    call.StartsWith('Q')
                        || call.StartsWith('q')
                        || call.StartsWith("0Q", StringComparison.OrdinalIgnoreCase),
                    $"{file}:{lineNumber} has a callsign that is not a Q pseudonym: {call}"
                );
            }
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void TextMentionsNoRealCallsigns(string file)
    {
        // Comments, statuses and messages mention stations ("de N0CALL-10", "via N0DIG"), which the anonymizer maps
        // like header callsigns. Position bytes, Mic-E data and base-91 telemetry are binary and can look like a
        // callsign by chance, so they are skipped; so is C4FM, a digital voice mode.
        var lineNumber = 0;
        foreach (var line in Lines(file))
        {
            lineNumber++;
            var info = CaptureLine().Match(line).Groups["info"].Value;
            if (info.Length == 0 || info[0] is '`' or '\'' or (char)0x1C or (char)0x1D)
            {
                continue;
            }

            // Text after a position starts beyond its bytes; matching from there still sees the whole word.
            var text = CommentTelemetry().Replace(info, match => new string(' ', match.Length));
            var start = info[0] is '!' or '=' or '/' or '@' or ';' or ')' ? Math.Min(20, text.Length) : 0;
            foreach (Match mention in TextCallsign().Matches(text, start))
            {
                var call = mention.Groups["call"].Value;
                Assert.True(
                    call.StartsWith('Q') || call.StartsWith("0Q", StringComparison.Ordinal) || call == "C4FM",
                    $"{file}:{lineNumber} mentions a callsign that is not a Q pseudonym: {call}"
                );
            }
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void EmailAddressesAreMasked(string file)
    {
        // Only the domain is checked: a mask can sit right after position bytes ("…53Whxxxx.xx0xxx@xxxxx.xxx"),
        // so the local part's start is ambiguous, but a real address always has a real domain.
        // Compressed position bytes are base-91 data that can look like one ("!L7@h.Ps__&"), so they are skipped.
        foreach (var line in Lines(file))
        {
            var info = CaptureLine().Match(line).Groups["info"];
            var position = CompressedPosition().Match(info.Value);
            var text = position.Success
                ? line[..info.Index] + new string(' ', position.Length) + line[(info.Index + position.Length)..]
                : line;
            foreach (Match domain in EmailDomain().Matches(text))
            {
                Assert.True(
                    MaskedDomain().IsMatch(domain.Value),
                    $"{file}: unmasked e-mail domain {domain.Value} in {line}"
                );
            }
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void EachFileIsDocumentedInTheCorpusReadme(string file)
    {
        var readme = File.ReadAllText(Path.Combine(_directory, "README.md"));

        Assert.Contains($"`{file}`", readme, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Lines(string file) =>
        File.ReadLines(Path.Combine(_directory, file), Encoding.Latin1).Where(line => line.Length > 0);

    [GeneratedRegex(
        @"^(?<stamp>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z) (?<source>[^>:,\s]+)>(?<dest>[^,:]+)(?<path>(?:,[^,:]+)*):(?<info>.*)$"
    )]
    private static partial Regex CaptureLine();

    [GeneratedRegex("^(?:[A-Z]{1,2}|[0-9][A-Z]|[A-Z][0-9])[0-9][A-Z]{1,4}$", RegexOptions.IgnoreCase)]
    private static partial Regex Callsign();

    [GeneratedRegex(
        @"^(?:WIDE[1-7](?:-[1-7])?|TRACE[1-7]?(?:-[1-7])?|TEMP[1-7]-[1-7]|qA[A-Za-z]|T2[A-Z0-9]+|[A-Z]{2}[1-7]-[1-7]|AMBCWOP-\d+|WLNK-\d+|CWOP-\d+|CORE-\d+)$"
    )]
    private static partial Regex Generic();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?<call>[A-Z]{1,2}[0-9][A-Z]{1,4}|[0-9][A-Z][0-9][A-Z]{1,4})(?:-[0-9]{1,2})?(?![A-Za-z0-9])"
    )]
    private static partial Regex TextCallsign();

    [GeneratedRegex(@"\|[!-{]{4,14}\|")]
    private static partial Regex CommentTelemetry();

    [GeneratedRegex(@"(?<=[A-Za-z0-9._%+-])@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+")]
    private static partial Regex EmailDomain();

    // A compressed position report: data type, optional timestamp, table, 4+4 base-91 bytes, symbol, cs, T.
    [GeneratedRegex(@"^[!=/@](?:[0-9]{6}[zh/])?[/\\A-Za-j][!-{]{8}.[!-{ ]{3}")]
    private static partial Regex CompressedPosition();

    [GeneratedRegex(@"^@[x0-]+(?:\.[x0-]+)+$")]
    private static partial Regex MaskedDomain();
}
