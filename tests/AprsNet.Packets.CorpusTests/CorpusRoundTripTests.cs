namespace AprsNet.Packets.CorpusTests;

/// <summary>
/// Rebuilds real packets from their decoded payloads with <see cref="AprsPacket.Create"/>: the information field must
/// come back byte for byte, for every payload type the encoder supports, apart from the parser's own normalisations.
/// </summary>
public class CorpusRoundTripTests
{
    [Fact]
    public void PacketsRebuildByteForByte()
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
                    is not {
                        HasHeader: true,
                        Outcome: ParseOutcome.Parsed,
                        Payload: StatusReport or MessagePayload or PositionReport
                    }
                )
                {
                    continue;
                }

                checkedPackets++;
                try
                {
                    var rebuilt = AprsPacket.Create(packet.Source, packet.Destination, packet.Path, packet.Payload);
                    // Create writes the canonical form of what the parser normalises: addressees padded to 9
                    // characters, upper-case locators and hemispheres, a timestamp dropped for being broken, and
                    // ambiguity applied to both coordinates. Those packets only need the same decoded fields, which
                    // Create checks itself.
                    var normalised =
                        packet.Issues.Any(i =>
                            i.Code
                                is ParseIssueCode.UnpaddedAddressee
                                    or ParseIssueCode.LowercaseHemisphere
                                    or ParseIssueCode.InvalidTimestamp
                        )
                        || packet.Payload
                            is StatusReport { MaidenheadLocator: not null }
                                or PositionReport { Position.Ambiguity: > 0 };
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

        Assert.True(checkedPackets > 30_000, $"only {checkedPackets} packets");
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {checkedPackets}:\n" + string.Join("\n", failures.Take(60))
        );
    }

    [Fact]
    public void PositionsRebuiltFromTheirValuesDecodeTheSame()
    {
        // The test above reuses the bytes a parsed position arrived with for its data extension. This one copies
        // only the values a caller can set into a new report, so every extension is encoded from its values; Create
        // checks that the result decodes to the same values within the format's precision.
        var checkedPackets = 0;
        var failures = new List<string>();
        foreach (var file in CorpusFileTests.CorpusFiles().Select(row => row.Data))
        {
            foreach (var (lineNumber, bytes) in CorpusParsingTests.Packets(file))
            {
                var packet = AprsPacket.Parse(bytes);
                if (
                    packet is not { HasHeader: true, Outcome: ParseOutcome.Parsed, Payload: PositionReport parsed }
                    || parsed.Weather is not null
                    || parsed.Datum is not null
                    || parsed.Area is not null
                    || parsed.DirectionFinding is not null
                    || parsed.Comment.Any(char.IsControl)
                )
                {
                    continue; // fields a caller cannot set yet, or control bytes the encoder refuses in new text
                }

                checkedPackets++;
                var copy = new PositionReport
                {
                    Position = parsed.Position,
                    Symbol = parsed.Symbol,
                    Timestamp = parsed.Timestamp,
                    IsMessagingCapable = parsed.IsMessagingCapable,
                    IsCompressed = parsed.IsCompressed,
                    Course = parsed.Course,
                    SpeedKnots = parsed.SpeedKnots,
                    AltitudeFeet = parsed.AltitudeFeet,
                    PowerHeightGain = parsed.PowerHeightGain,
                    RangeMiles = parsed.RangeMiles,
                    DirectionFindingStrength = parsed.DirectionFindingStrength,
                    Comment = parsed.Comment,
                };
                try
                {
                    AprsPacket.Create(packet.Source, packet.Destination, packet.Path, copy);
                }
                catch (ArgumentException ex)
                {
                    failures.Add($"{file}:{lineNumber} {ex.Message} {packet}");
                }
            }
        }

        Assert.True(checkedPackets > 20_000, $"only {checkedPackets} packets");
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {checkedPackets}:\n" + string.Join("\n", failures.Take(60))
        );
    }
}
