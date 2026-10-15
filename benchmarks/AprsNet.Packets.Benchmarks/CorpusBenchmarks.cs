using System.Text;
using AprsNet.Packets;
using BenchmarkDotNet.Attributes;

namespace AprsNet.Packets.Benchmarks;

/// <summary>
/// Parsing real traffic's mix of types, lengths and mistakes: the first <see cref="PacketCount"/> corpus packets, files
/// in name order. New captures are dated, so they sort last and the sample stays the same as the corpus grows. Results
/// are per packet (<see cref="BenchmarkAttribute.OperationsPerInvoke"/> is the sample size).
/// </summary>
[MemoryDiagnoser]
public class CorpusBenchmarks
{
    private const int PacketCount = 10_000;

    private byte[][] _packets = [];

    [GlobalSetup]
    public void Load()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "corpus");
        _packets =
        [
            .. Directory
                .EnumerateFiles(directory, "*.log")
                .Order(StringComparer.Ordinal)
                .SelectMany(path => File.ReadAllLines(path, Encoding.Latin1)) // every byte as one character
                .Where(line => line.Length > 0)
                .Select(line => Encoding.Latin1.GetBytes(line[(line.IndexOf(' ', StringComparison.Ordinal) + 1)..]))
                .Take(PacketCount),
        ];
        if (_packets.Length != PacketCount)
        {
            throw new InvalidOperationException($"The corpus has only {_packets.Length} packets.");
        }
    }

    [Benchmark(OperationsPerInvoke = PacketCount)]
    public int ParseCorpus()
    {
        var parsed = 0;
        foreach (var packet in _packets)
        {
            parsed += AprsPacket.Parse(packet).Outcome == ParseOutcome.Parsed ? 1 : 0;
        }

        return parsed;
    }
}
