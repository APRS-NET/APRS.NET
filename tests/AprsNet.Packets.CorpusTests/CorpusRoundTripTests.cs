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
        var micE = 0;
        var objectsAndItems = 0;
        var weather = 0;
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
                            Payload: StatusReport
                                or MessagePayload
                                or PositionReport
                                or MicEReport
                                or ObjectReport
                                or ItemReport
                                or WeatherReport
                        }
                    || packet.Payload is MicEReport { Message: MicEMessage.Unknown } or ObjectReport { Timestamp: null }
                )
                {
                    // Not encodable: a Mic-E message that mixes standard and custom bits, or an object whose broken
                    // timestamp the parser dropped.
                    continue;
                }

                checkedPackets++;
                micE += packet.Payload is MicEReport ? 1 : 0;
                objectsAndItems += packet.Payload is ObjectReport or ItemReport ? 1 : 0;
                weather += packet.Payload is WeatherReport or PositionedPayload { Weather: not null } ? 1 : 0;
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
                                or PositionedPayload { Position.Ambiguity: > 0 }
                                and not MicEReport;
                    if (
                        !normalised
                        && (
                            !rebuilt.Information.Span.SequenceEqual(packet.Information.Span)
                            || rebuilt.Destination != packet.Destination
                        )
                    )
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
        Assert.True(micE > 2_500, $"only {micE} Mic-E packets");
        Assert.True(objectsAndItems > 5_000, $"only {objectsAndItems} objects and items");
        Assert.True(weather > 5_000, $"only {weather} weather reports");
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
                    Weather = parsed.Weather,
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

    [Fact]
    public void MicEReportsRebuiltFromTheirValuesDecodeTheSame()
    {
        // As above for Mic-E: the destination, longitude, speed and course bytes are written from the values, and
        // Create checks that a receiver decodes the same values.
        var checkedPackets = 0;
        var failures = new List<string>();
        foreach (var file in CorpusFileTests.CorpusFiles().Select(row => row.Data))
        {
            foreach (var (lineNumber, bytes) in CorpusParsingTests.Packets(file))
            {
                var packet = AprsPacket.Parse(bytes);
                if (
                    packet is not { HasHeader: true, Outcome: ParseOutcome.Parsed, Payload: MicEReport parsed }
                    || parsed.Message == MicEMessage.Unknown
                    || parsed.Datum is not null
                    || parsed.SpeedKnots > 670
                    || parsed.Comment.Any(char.IsControl)
                )
                {
                    continue; // values a caller cannot set or send
                }

                checkedPackets++;
                var copy = new MicEReport
                {
                    Position = parsed.Position,
                    Symbol = parsed.Symbol,
                    Message = parsed.Message,
                    IsCurrentFix = parsed.IsCurrentFix,
                    IsMessagingCapable = parsed.IsMessagingCapable,
                    Course = parsed.Course,
                    SpeedKnots = parsed.SpeedKnots,
                    AltitudeFeet = parsed.AltitudeFeet,
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

        Assert.True(checkedPackets > 2_000, $"only {checkedPackets} packets");
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {checkedPackets}:\n" + string.Join("\n", failures.Take(60))
        );
    }

    [Fact]
    public void ObjectsAndItemsRebuiltFromTheirValuesDecodeTheSame()
    {
        // As for positions: only the values a caller can set are copied into a new object or item.
        var checkedPackets = 0;
        var failures = new List<string>();
        foreach (var file in CorpusFileTests.CorpusFiles().Select(row => row.Data))
        {
            foreach (var (lineNumber, bytes) in CorpusParsingTests.Packets(file))
            {
                var packet = AprsPacket.Parse(bytes);
                if (
                    packet is not { HasHeader: true, Outcome: ParseOutcome.Parsed, Payload: PositionedPayload parsed }
                    || parsed is not (ObjectReport { Timestamp: not null } or ItemReport)
                    || parsed.Datum is not null
                    || parsed.Area is not null
                    || parsed.DirectionFinding is not null
                    || parsed.Comment.Any(char.IsControl)
                )
                {
                    continue; // fields a caller cannot set yet, or control bytes the encoder refuses in new text
                }

                checkedPackets++;
                AprsPayload copy = parsed switch
                {
                    ObjectReport o => new ObjectReport
                    {
                        Name = o.Name,
                        IsLive = o.IsLive,
                        Timestamp = o.Timestamp,
                        Position = o.Position,
                        Symbol = o.Symbol,
                        IsCompressed = o.IsCompressed,
                        Course = o.Course,
                        SpeedKnots = o.SpeedKnots,
                        AltitudeFeet = o.AltitudeFeet,
                        PowerHeightGain = o.PowerHeightGain,
                        RangeMiles = o.RangeMiles,
                        DirectionFindingStrength = o.DirectionFindingStrength,
                        Weather = o.Weather,
                        Comment = o.Comment,
                    },
                    ItemReport i => new ItemReport
                    {
                        Name = i.Name,
                        IsLive = i.IsLive,
                        Position = i.Position,
                        Symbol = i.Symbol,
                        IsCompressed = i.IsCompressed,
                        Course = i.Course,
                        SpeedKnots = i.SpeedKnots,
                        AltitudeFeet = i.AltitudeFeet,
                        PowerHeightGain = i.PowerHeightGain,
                        RangeMiles = i.RangeMiles,
                        DirectionFindingStrength = i.DirectionFindingStrength,
                        Weather = i.Weather,
                        Comment = i.Comment,
                    },
                    _ => throw new InvalidOperationException(),
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

        Assert.True(checkedPackets > 3_000, $"only {checkedPackets} packets");
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {checkedPackets}:\n" + string.Join("\n", failures.Take(60))
        );
    }

    [Fact]
    public void WeatherReportsRebuiltFromTheirValuesDecodeTheSame()
    {
        // Positionless reports, written in the canonical field order from the values alone.
        var checkedPackets = 0;
        var failures = new List<string>();
        foreach (var file in CorpusFileTests.CorpusFiles().Select(row => row.Data))
        {
            foreach (var (lineNumber, bytes) in CorpusParsingTests.Packets(file))
            {
                var packet = AprsPacket.Parse(bytes);
                if (
                    packet is not { HasHeader: true, Outcome: ParseOutcome.Parsed, Payload: WeatherReport parsed }
                    || parsed.Timestamp is null
                    || parsed.Weather.SnowLast24HoursInches is not null
                    || parsed.Comment.Any(char.IsControl)
                    || parsed.Comment.StartsWith('.')
                )
                {
                    // No readable timestamp (a new report needs one), snowfall a positionless report cannot carry,
                    // control bytes refused in new text, or a comment
                    // whose leading dots would join the dotted field written before it (a station that leaves out
                    // the wind sends ".../...g..." as its comment).
                    continue;
                }

                checkedPackets++;
                var copy = new WeatherReport
                {
                    Timestamp = parsed.Timestamp,
                    Weather = parsed.Weather,
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

        Assert.True(checkedPackets > 50, $"only {checkedPackets} packets");
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {checkedPackets}:\n" + string.Join("\n", failures.Take(60))
        );
    }
}
