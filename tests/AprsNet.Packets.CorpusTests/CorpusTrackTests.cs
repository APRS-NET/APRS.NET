using System.Globalization;
using System.Text;

namespace AprsNet.Packets.CorpusTests;

/// <summary>
/// Replays every capture through <see cref="TrackAnalysis"/>, one track per station, object and item, after a
/// <see cref="DuplicateFilter"/> as a map would: real traffic must not throw, and should rarely look impossible.
/// </summary>
public class CorpusTrackTests(ITestOutputHelper output)
{
    private static readonly string _directory = Path.Combine(AppContext.BaseDirectory, "corpus");

    [Fact]
    public void RealTracksAreMostlyClean()
    {
        var reports = 0;
        var counts = Enum.GetValues<TrackAnomalies>().Where(f => f != TrackAnomalies.None).ToDictionary(f => f, _ => 0);
        foreach (var row in CorpusFileTests.CorpusFiles())
        {
            var clock = new ReplayClock();
            var duplicates = new DuplicateFilter(timeProvider: clock);
            var tracks = new Dictionary<string, List<TrackReport>>(StringComparer.Ordinal);
            foreach (var (received, bytes) in TimedPackets(row.Data))
            {
                clock.Now = received;
                var packet = AprsPacket.Parse(bytes);
                if (!duplicates.TryAccept(packet) || TrackReport.FromPacket(packet, received) is not { } report)
                {
                    continue;
                }

                var key = packet.Payload switch
                {
                    ObjectReport o => ";" + o.Name,
                    ItemReport i => ")" + i.Name,
                    _ => packet.Source!.Value,
                };
                if (!tracks.TryGetValue(key, out var track))
                {
                    tracks[key] = track = [];
                }

                var check = TrackAnalysis.Check(track, report);
                reports++;
                foreach (var flag in counts.Keys.Where(f => check.Anomalies.HasFlag(f)))
                {
                    counts[flag]++;
                }

                track.Add(report);
            }
        }

        foreach (var (flag, count) in counts)
        {
            output.WriteLine($"{flag}: {count} of {reports} ({100.0 * count / reports:0.00}%)");
        }

        Assert.True(reports > 10_000, $"only {reports} position reports in the corpus");

        // Floors that catch a regression, from 51,234 reports: 12.6% repeated positions (stations beaconing within
        // 2 minutes, and late copies), 0.29% impossible speeds (mostly items and objects that share a name), 1.19% rapid
        // repeats and 0.09% times running backwards.
        Assert.True(counts[TrackAnomalies.RepeatedPosition] < reports * 0.15);
        Assert.True(counts[TrackAnomalies.ImpossibleSpeed] < reports * 0.01);
        Assert.True(counts[TrackAnomalies.RapidRepeat] < reports * 0.03);
        Assert.True(counts[TrackAnomalies.TimeBackwards] < reports * 0.01);
    }

    private static IEnumerable<(DateTimeOffset Received, byte[] Bytes)> TimedPackets(string file)
    {
        foreach (var line in File.ReadLines(Path.Combine(_directory, file), Encoding.Latin1))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space > 0)
            {
                yield return (
                    DateTimeOffset.Parse(line[..space], CultureInfo.InvariantCulture),
                    Encoding.Latin1.GetBytes(line[(space + 1)..])
                );
            }
        }
    }

    private sealed class ReplayClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
