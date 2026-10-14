namespace AprsNet.Packets.CorpusTests;

/// <summary>
/// Rebuilds real packets from their decoded payloads with <see cref="AprsPacket.Create"/>: the information field must
/// come back byte for byte, for every payload type the encoder supports, apart from the parser's own normalisations.
/// </summary>
public class CorpusRoundTripTests
{
    [Fact]
    public void StatusReportsAndMessagesRebuildByteForByte()
    {
        var checkedPackets = 0;
        var failures = new List<string>();
        foreach (var file in CorpusFileTests.CorpusFiles().Select(row => row.Data))
        {
            foreach (var (lineNumber, bytes) in CorpusParsingTests.Packets(file))
            {
                var packet = AprsPacket.Parse(bytes);
                if (
                    packet
                    is not { HasHeader: true, Outcome: ParseOutcome.Parsed, Payload: StatusReport or MessagePayload }
                )
                {
                    continue;
                }

                checkedPackets++;
                try
                {
                    var rebuilt = AprsPacket.Create(packet.Source, packet.Destination, packet.Path, packet.Payload);
                    // Create re-pads addressees to 9 characters and upper-cases locators, which the parser already
                    // normalises; those packets only need the same decoded fields, which Create checks itself.
                    var normalised =
                        packet.Issues.Any(i => i.Code == ParseIssueCode.UnpaddedAddressee)
                        || packet.Payload is StatusReport { MaidenheadLocator: not null };
                    if (!normalised && !rebuilt.Information.Span.SequenceEqual(packet.Information.Span))
                    {
                        failures.Add($"{file}:{lineNumber} differs:\n  was {packet}\n  now {rebuilt}");
                    }
                }
                catch (ArgumentException ex)
                {
                    failures.Add($"{file}:{lineNumber} {ex.Message} {packet}");
                }
            }
        }

        Assert.True(checkedPackets > 8_000, $"only {checkedPackets} packets");
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {checkedPackets}:\n" + string.Join("\n", failures.Take(60))
        );
    }
}
