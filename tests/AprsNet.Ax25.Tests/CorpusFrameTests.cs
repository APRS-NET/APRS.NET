using System.Text;
using AprsNet.Packets;

namespace AprsNet.Ax25.Tests;

/// <summary>
/// Converts real packets to AX.25 frames and back. Each corpus packet keeps only the RF part of its path (before the
/// APRS-IS q-construct), as an IGate heard it; packets whose stations AX.25 cannot carry are skipped. Stations are
/// compared as AX.25 addresses, since <c>N0CALL-0</c> and <c>N0CALL</c> are the same address on the air.
/// </summary>
public class CorpusFrameTests
{
    [Fact]
    public void RealPacketsSurviveAFrameRoundTrip()
    {
        var checkedPackets = 0;
        var failures = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "corpus"), "*.log"))
        {
            foreach (var line in File.ReadLines(file, Encoding.Latin1))
            {
                var parsed = AprsPacket.Parse(line[(line.IndexOf(' ', StringComparison.Ordinal) + 1)..]);
                if (!parsed.HasHeader)
                {
                    continue;
                }

                var rf = parsed.Path.TakeWhile(e => e.Kind != PathEntryKind.QConstruct).ToList();
                var heard = AprsPacket.Parse([
                    .. Encoding.Latin1.GetBytes(
                        $"{parsed.Source}>{parsed.Destination}{string.Concat(rf.Select(e => "," + e))}:"
                    ),
                    .. parsed.Information.Span,
                ]);
                Ax25Frame frame;
                try
                {
                    frame = Ax25Frame.FromAprsPacket(heard);
                }
                catch (ArgumentException)
                {
                    continue; // a station or alias AX.25 cannot carry, as on APRS-IS-only traffic
                }

                checkedPackets++;
                var back = Ax25Frame.TryDecode(frame.GetBytes(), out var decoded, out var error)
                    ? decoded.ToAprsPacket()
                    : null;
                if (back is null)
                {
                    failures.Add($"did not decode ({error}): {heard}");
                }
                else if (
                    !back.Information.Span.SequenceEqual(heard.Information.Span)
                    || Address(back.Source!) != Address(heard.Source!)
                    || Address(back.Destination!) != Address(heard.Destination!)
                    || back.Type != heard.Type
                    || back.Outcome != heard.Outcome
                    || !back.Path!.Select(e => Address(e.Station))
                        .SequenceEqual(heard.Path!.Select(e => Address(e.Station)))
                )
                {
                    failures.Add($"changed:\n  was {heard}\n  now {back}");
                }
            }
        }

        // AX.25 has one spelling of SSID 0, so N0CALL-0 comes back as N0CALL.
        static string Address(Callsign station) =>
            Ax25Address.TryParse(station.Value, out var address) ? address.ToString() : station.Value;

        Assert.True(checkedPackets > 30_000, $"only {checkedPackets} packets");
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {checkedPackets}:\n" + string.Join("\n", failures.Take(30))
        );
    }
}
