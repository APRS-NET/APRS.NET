using System.Text;
using System.Text.RegularExpressions;
using AprsNet.Packets;

namespace AprsNet.Filters.Tests;

/// <summary>
/// The corpus captures were filtered by APRS-IS servers, so the same filter evaluated here must agree with the server
/// on the packets it sent.
/// </summary>
public sealed partial class CorpusFilterTests(ITestOutputHelper output)
{
    private static readonly string CorpusDirectory = Path.Combine(AppContext.BaseDirectory, "corpus");

    public static TheoryData<string> RangeCaptures() =>
        [
            .. Directory
                .EnumerateFiles(CorpusDirectory, "*.log")
                .Select(path => Path.GetFileName(path))
                .Where(name => RangeFileName().IsMatch(name))
                .Order(StringComparer.Ordinal),
        ];

    [Theory]
    [MemberData(nameof(RangeCaptures))]
    public void PositionsInARangeCaptureAreInRange(string file)
    {
        var filter = AprsFilter.Parse(ReadmeFilter(file));
        var positioned = Packets(file).Where(p => p.Payload is PositionedPayload { IsPositionUnknown: false }).ToList();
        var outside = positioned.Where(p => !filter.Matches(p)).ToList();
        foreach (var packet in outside.Take(5))
        {
            output.WriteLine(packet.ToString());
        }

        // The server also passes packets by their sender's last position, so a mobile that has just left arrives from
        // outside the circle, and a position with ambiguity on the edge can fall either side: six packets in the whole corpus.
        Assert.True(
            outside.Count <= positioned.Count / 100,
            $"{outside.Count} of {positioned.Count} positions are outside {filter}."
        );
    }

    [Theory]
    [InlineData("2026-10-13-type-items.log", "t/i")]
    [InlineData("2026-10-13-type-messages.log", "t/m")]
    [InlineData("2026-10-13-type-user.log", "t/u")]
    [InlineData("2026-10-13-type-nws.log", "t/n")]
    [InlineData("2026-10-14-type-status.log", "t/s")]
    [InlineData("2026-10-14-type-telemetry.log", "t/t")]
    [InlineData("2026-10-13-type-weather-positionless.log", "t/w")]
    public void ATypeCaptureMatchesItsTypeFilter(string file, string text)
    {
        var filter = AprsFilter.Parse(text);
        var packets = Packets(file).Where(p => p.Outcome == ParseOutcome.Parsed).ToList();
        var missed = packets.Where(p => !filter.Matches(p)).ToList();
        foreach (var packet in missed.Take(5))
        {
            output.WriteLine(packet.ToString());
        }

        Assert.True(missed.Count == 0, $"{missed.Count} of {packets.Count} do not match {text}.");
    }

    private static IEnumerable<AprsPacket> Packets(string file) =>
        File.ReadLines(Path.Combine(CorpusDirectory, file), Encoding.Latin1)
            .Where(line => line.Length > 0)
            .Select(line => AprsPacket.Parse(Encoding.Latin1.GetBytes(line[(line.IndexOf(' ') + 1)..])));

    /// <summary>The filter a capture was made with, from its row in the corpus README.</summary>
    private static string ReadmeFilter(string file)
    {
        var readme = File.ReadAllText(Path.Combine(CorpusDirectory, "README.md"));
        var row = readme.Split('\n').Single(line => line.Contains($"`aprs-is/{file}`", StringComparison.Ordinal));
        return ReadmeRangeFilter().Match(row).Groups[1].Value;
    }

    [GeneratedRegex(@"-r\d+km\.log$")]
    private static partial Regex RangeFileName();

    [GeneratedRegex(@"filter `(r/[-0-9./]+)`")]
    private static partial Regex ReadmeRangeFilter();
}
