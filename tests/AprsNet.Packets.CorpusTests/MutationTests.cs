namespace AprsNet.Packets.CorpusTests;

/// <summary>
/// Feeds the parser and encoder corpus packets with random damage: flipped, dropped, inserted and swapped bytes,
/// truncation, and control or high bytes in header and body alike. The seed is fixed, so a failure reproduces; the
/// message names the input. Real traffic is the seed because random bytes alone rarely get past the header. To search
/// further locally, set <c>APRSNET_MUTATION_SEED</c> and <c>APRSNET_MUTATIONS</c> (per packet).
/// </summary>
public class MutationTests
{
    private static readonly int Seed = Setting("APRSNET_MUTATION_SEED") ?? 20261015;
    private static readonly int MutationsPerPacket = Setting("APRSNET_MUTATIONS") ?? 3;

    private static int? Setting(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : null;

    [Fact]
    public void ParsingDamagedPacketsNeverThrowsAndReportsConsistently()
    {
        var random = new Random(Seed);
        var checkedPackets = 0;
        var failures = new List<string>();
        foreach (var file in CorpusFileTests.CorpusFiles().Select(row => row.Data))
        {
            foreach (var (_, bytes) in CorpusParsingTests.Packets(file))
            {
                for (var i = 0; i < MutationsPerPacket; i++)
                {
                    var damaged = Mutate(bytes, random);
                    checkedPackets++;
                    if (Check(damaged) is { } problem)
                    {
                        failures.Add($"{problem}: {Convert.ToHexString(damaged)}");
                    }
                }
            }
        }

        Assert.True(checkedPackets > 100_000, $"only {checkedPackets} packets");
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {checkedPackets}:\n" + string.Join("\n", failures.Take(30))
        );
    }

    /// <summary>What is wrong with how <paramref name="line"/> parses and rebuilds, or null.</summary>
    private static string? Check(byte[] line)
    {
        AprsPacket packet;
        try
        {
            packet = AprsPacket.Parse(line);
        }
        catch (Exception ex)
        {
            return $"Parse threw {ex.GetType().Name}: {ex.Message}";
        }

        var hasError = packet.Issues.Any(i => i.Severity == ParseIssueSeverity.Error);
        var expected =
            hasError ? ParseOutcome.Malformed
            : packet.Payload is null ? ParseOutcome.Unsupported
            : ParseOutcome.Parsed;
        if (packet.Outcome != expected)
        {
            return $"outcome {packet.Outcome}, expected {expected}";
        }

        var outside = packet.Issues.Where(i => i.Offset < 0 || i.Offset > packet.Raw.Length).ToList();
        if (outside.Count > 0)
        {
            return $"issue {outside[0].Code} at {outside[0].Offset}, outside {packet.Raw.Length} bytes";
        }

        if (packet is not { Outcome: ParseOutcome.Parsed, HasHeader: true })
        {
            return null;
        }

        try
        {
            AprsPacket.Create(packet.Source, packet.Destination, packet.Path, packet.Payload!);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Refusing a payload is fine: damage can make a field the encoder cannot write.
        }
        catch (Exception ex)
        {
            return $"Create threw {ex.GetType().Name}: {ex.Message}";
        }

        return null;
    }

    private static readonly byte[] _interesting =
        "\0\r\n\x1C\x1D,:>*{}|!/\\`'_#;@=$T~\x7F\x80\xB0\xFF0123456789 "u8.ToArray();

    private static byte[] Mutate(byte[] original, Random random)
    {
        var bytes = new List<byte>(original);
        var edits = random.Next(1, 4);
        for (var e = 0; e < edits && bytes.Count > 0; e++)
        {
            var at = random.Next(bytes.Count);
            switch (random.Next(6))
            {
                case 0:
                    bytes[at] = (byte)random.Next(256);
                    break;
                case 1:
                    bytes[at] = _interesting[random.Next(_interesting.Length)];
                    break;
                case 2:
                    bytes.RemoveAt(at);
                    break;
                case 3:
                    bytes.Insert(at, _interesting[random.Next(_interesting.Length)]);
                    break;
                case 4:
                    bytes.RemoveRange(at, bytes.Count - at); // truncate
                    break;
                default:
                    var length = Math.Min(random.Next(1, 8), bytes.Count - at);
                    var chunk = bytes.GetRange(at, length);
                    bytes.RemoveRange(at, length);
                    bytes.InsertRange(random.Next(bytes.Count + 1), chunk);
                    break;
            }
        }

        return [.. bytes];
    }
}
