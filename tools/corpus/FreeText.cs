#:property TargetFramework=net10.0
#:property TargetFrameworks=
#:property PublishAot=false

// Prints each distinct stretch of free text in anonymized capture files, with the station that sent it first, so a
// person can read it for personal names, street addresses and phone numbers before the files are committed. Anything
// found goes in the mapping file's Literals section, and the capture is anonymized again.
//
//   dotnet run tools/corpus/FreeText.cs -- tests/corpus/aprs-is/2026-10-15-*.log     (from the repository root)
//
// Positions, timestamps and other machine-made fields are skipped: only runs of six or more letters, spaces and
// punctuation with at least three lower-case letters are printed, which is where people write about themselves.

using System.Text;
using System.Text.RegularExpressions;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run tools/corpus/FreeText.cs -- <anonymized capture>...");
    return 3;
}

var text = new Regex(@"[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ .,'&/-]{5,}");
var lowerCase = new Regex("[a-zß-ÿ]");
var firstSeen = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var path in args)
{
    foreach (var line in File.ReadLines(path, Encoding.Latin1))
    {
        var packet = line[(line.IndexOf(' ') + 1)..];
        var info = packet[(packet.IndexOf(':') + 1)..];
        var source = packet.Split('>')[0];
        foreach (Match match in text.Matches(info))
        {
            var stretch = match.Value.Trim();
            if (lowerCase.Count(stretch) >= 3)
            {
                firstSeen.TryAdd(stretch, source);
            }
        }
    }
}

// Latin-1 holds UTF-8 text as its raw bytes; print it as UTF-8 so accented and CJK text reads correctly.
foreach (var (stretch, source) in firstSeen.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
{
    var readable = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(stretch));
    Console.WriteLine($"{source, -12} {readable}");
}

Console.Error.WriteLine($"{firstSeen.Count} distinct stretches of text.");
return 0;
