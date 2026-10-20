using System.Globalization;
using System.Text;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.IGate.Tests;

/// <summary>
/// Replays what other IGates gated in the corpus as if this IGate had heard it on RF: each line's header up to its
/// <c>qAR</c> or <c>qAO</c>, then its information field. The IGate should send each one with its own q-construct and
/// the path unchanged, once per duplicate window.
/// </summary>
public sealed class CorpusGatingTests
{
    private static readonly string _directory = Path.Combine(AppContext.BaseDirectory, "corpus", "aprs-is");

    [Fact]
    public void GatesWhatOtherIGatesGatedWithThePathAsHeard()
    {
        var gated = 0;
        var refused = new Dictionary<GateRefusal, int>();
        foreach (var file in Directory.GetFiles(_directory, "*.log").Order(StringComparer.Ordinal))
        {
            var time = new FakeTimeProvider();
            var gate = new RfToInternetGate(Callsign.Parse("N0CALL-10"), new() { TimeProvider = time });
            var lastSent = new Dictionary<PacketIdentity, DateTimeOffset>();
            foreach (var line in File.ReadLines(file, Encoding.Latin1).Where(line => line.Length > 0))
            {
                var stamp = DateTimeOffset.Parse(line[..24], CultureInfo.InvariantCulture);
                if (stamp > time.GetUtcNow())
                {
                    time.SetUtcNow(stamp);
                }

                if (HeardOnRf(line[25..]) is not { } heard)
                {
                    continue;
                }

                var decision = gate.Gate(AprsPacket.Parse(heard));
                if (decision.IsGated)
                {
                    gated++;
                    var colon = heard.IndexOf(':', StringComparison.Ordinal);
                    var end = heard.IndexOfAny(['\r', '\n', '\0'], colon); // a few IGates pass a NUL on
                    var expected = heard[..colon] + ",qAO,N0CALL-10" + heard[colon..(end < 0 ? heard.Length : end)];
                    Assert.Equal(expected, decision.Packet.ToString());
                    lastSent[PacketIdentity.Of(decision.Packet)] = time.GetUtcNow();
                    continue;
                }

                var refusal = decision.Refusal!.Value;
                refused[refusal] = refused.GetValueOrDefault(refusal) + 1;
                if (refusal == GateRefusal.Duplicate)
                {
                    // A copy is refused only within 30 seconds of the packet the IGate sent.
                    var sent = lastSent[PacketIdentity.Of(AprsPacket.Parse(heard))];
                    Assert.True(time.GetUtcNow() - sent < TimeSpan.FromSeconds(30), heard);
                }
            }
        }

        // Other IGates gate what this one would refuse only rarely, such as LoRa noise that starts with '}'.
        var others = refused.Where(r => r.Key != GateRefusal.Duplicate).Sum(r => r.Value);
        Assert.True(gated > 10_000, $"only {gated} lines gated");
        Assert.True(others * 100 < gated, string.Join(", ", refused.Select(r => $"{r.Key} {r.Value}")));
    }

    /// <summary>
    /// The packet as an IGate heard it on RF: the line without its q-construct and IGate, for lines gated from RF with
    /// <c>qAR</c> or <c>qAO</c>; <see langword="null"/> for the rest.
    /// </summary>
    private static string? HeardOnRf(string line)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return null;
        }

        var header = line[..colon];
        var q = header.IndexOf(",qAR,", StringComparison.Ordinal);
        q = q >= 0 ? q : header.IndexOf(",qAO,", StringComparison.Ordinal);

        // An IGate's own beacon, sent over its internet connection, carries TCPIP and qAR too: nothing heard it on RF.
        return q < 0 || !PathAnalysis.FromPacket(AprsPacket.Parse(line)).IsGatedFromRadio
            ? null
            : header[..q] + line[colon..];
    }
}
