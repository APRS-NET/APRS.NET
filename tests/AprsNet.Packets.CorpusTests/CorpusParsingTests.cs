using System.Text;

namespace AprsNet.Packets.CorpusTests;

/// <summary>
/// Runs every captured packet through <see cref="AprsPacket.Parse(ReadOnlySpan{byte})"/>: real traffic must parse
/// without throwing, keep every byte, and have a well-formed header.
/// </summary>
public class CorpusParsingTests
{
    private static readonly string _directory = Path.Combine(AppContext.BaseDirectory, "corpus");

    // Header problems are bugs in our reading of real traffic; payload problems can be the sender's.
    private const ParseIssueCode _firstPayloadIssue = ParseIssueCode.InvalidTimestamp;

    public static TheoryData<string> CorpusFiles() => CorpusFileTests.CorpusFiles();

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void EveryPacketParsesAndKeepsItsBytes(string file)
    {
        foreach (var (lineNumber, bytes) in Packets(file))
        {
            var packet = AprsPacket.Parse(bytes);

            Assert.True(bytes.AsSpan().SequenceEqual(packet.Raw.Span), $"{file}:{lineNumber} changed its bytes");
            Assert.Equal(Encoding.Latin1.GetString(bytes), packet.ToString());
            Assert.True(packet.HasHeader, $"{file}:{lineNumber}: {string.Join(", ", packet.Issues)}");
            Assert.DoesNotContain(
                packet.Issues,
                i => i is { Severity: ParseIssueSeverity.Error, Code: < _firstPayloadIssue }
            );
        }
    }

    [Fact]
    public void AlmostEveryPositionDecodes()
    {
        // Real traffic includes broken positions (a weather template with no position, for example), so this is a
        // floor that catches regressions rather than a demand for perfection.
        var positions = CorpusFiles()
            .SelectMany(row => Packets(row.Data))
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Type == PacketType.Position)
            .ToList();
        var decoded = positions.Count(p => p.Payload is PositionReport);

        Assert.True(positions.Count > 5000, $"only {positions.Count} positions in the corpus");
        Assert.True(decoded >= positions.Count * 0.995, $"{decoded} of {positions.Count} positions decoded");
    }

    [Fact]
    public void EveryStatusDecodes()
    {
        var statuses = CorpusFiles()
            .SelectMany(row => Packets(row.Data))
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Type == PacketType.Status)
            .ToList();

        Assert.True(statuses.Count > 400, $"only {statuses.Count} status reports in the corpus");
        Assert.All(statuses, p => Assert.IsType<StatusReport>(p.Payload));
        Assert.Contains(statuses, p => ((StatusReport)p.Payload!).Timestamp is not null);
    }

    [Fact]
    public void MessagesDecodeUnlessTheyAreNotMessages()
    {
        var messages = CorpusFiles()
            .SelectMany(row => Packets(row.Data))
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Type == PacketType.Message)
            .ToList();

        Assert.True(messages.Count > 300, $"only {messages.Count} messages in the corpus");
        // A few stations put a position or a comment after the ':' data type, with no addressee at all.
        var broken = messages.Where(p => p.Outcome != ParseOutcome.Parsed).ToList();
        Assert.All(broken, p => Assert.Contains(p.Issues, i => i.Code == ParseIssueCode.InvalidAddressee));
        Assert.True(broken.Count * 100 < messages.Count, $"{broken.Count} of {messages.Count} messages failed");
        Assert.Contains(messages, p => p.Payload is TelemetryDefinition { Kind: TelemetryDefinitionKind.Equations });
        Assert.Contains(messages, p => p.Payload is MessagePayload { Kind: MessageKind.Acknowledgement });
        Assert.Contains(messages, p => p.Payload is MessagePayload { Kind: MessageKind.Bulletin });
        Assert.Contains(messages, p => p.Payload is MessagePayload { Kind: MessageKind.NwsBulletin });
        // SvxLink sends 8-character addressees and some bulletins pad past 9; both are read, with a warning.
        Assert.Contains(messages, p => p.Issues.Any(i => i.Code == ParseIssueCode.UnpaddedAddressee));
        Assert.All(
            messages.Where(p => p.Outcome == ParseOutcome.Parsed),
            p =>
                Assert.All(
                    p.Issues,
                    i =>
                        Assert.True(
                            i.Code is ParseIssueCode.UnpaddedAddressee or ParseIssueCode.InvalidTelemetryDefinition
                        )
                )
        );
    }

    [Fact]
    public void ObjectsAndItemsDecodeUnlessTheSenderBrokeThem()
    {
        var packets = CorpusFiles()
            .SelectMany(row => Packets(row.Data))
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Type is PacketType.Object or PacketType.Item)
            .ToList();

        // Real senders get some wrong: DMR gateway items at 003-0.00W or 99900.00N, minutes of 80, names over 9
        // characters (F/SISARGAS). Each must be reported as such, and they stay a small share.
        Assert.True(packets.Count > 900, $"only {packets.Count} objects and items in the corpus");
        var broken = packets.Where(p => p.Payload is null).ToList();
        Assert.All(
            broken,
            p =>
                Assert.Contains(
                    p.Issues,
                    i =>
                        i.Code
                            is ParseIssueCode.InvalidLatitude
                                or ParseIssueCode.InvalidLongitude
                                or ParseIssueCode.InvalidObject
                                or ParseIssueCode.InvalidItem
                                or ParseIssueCode.PositionTooShort
                )
        );
        Assert.True(broken.Count * 20 < packets.Count, $"{broken.Count} of {packets.Count} objects and items failed");
        Assert.Contains(packets, p => p.Payload is ObjectReport { IsLive: false });
        Assert.Contains(packets, p => p.Payload is ItemReport);
    }

    [Fact]
    public void MicEReportsDecodeUnlessTheDestinationIsBroken()
    {
        var reports = CorpusFiles()
            .SelectMany(row => Packets(row.Data))
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Type == PacketType.MicE)
            .ToList();

        Assert.True(reports.Count > 800, $"only {reports.Count} Mic-E reports in the corpus");
        // One Prague tracker's packets reach APRS-IS with a 7-character destination (UP0P413) after a bad digipeater.
        Assert.All(
            reports.Where(p => p.Payload is null),
            p =>
            {
                Assert.NotEqual(6, p.Destination!.Value.Split('-')[0].Length);
                Assert.Contains(p.Issues, i => i.Code == ParseIssueCode.InvalidMicEDestination);
            }
        );
        Assert.DoesNotContain(reports, p => p.Payload is not null && p.Issues.Count > 0);
        Assert.Contains(reports, p => p.Payload is MicEReport { Device: "TM-D710" });
        Assert.Contains(reports, p => p.Payload is MicEReport { Device: "FTM-400DR" });
    }

    [Fact]
    public void TelemetryReportsDecodeUnlessTheirBitsAreBroken()
    {
        var reports = CorpusFiles()
            .SelectMany(row => Packets(row.Data))
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Type == PacketType.Telemetry)
            .ToList();

        Assert.True(reports.Count > 80, $"only {reports.Count} telemetry reports in the corpus");
        // One Dallas station sends a single digital bit ("T#738,116,0,584,52,0,0") instead of eight.
        var broken = reports.Where(p => p.Payload is null).ToList();
        Assert.All(broken, p => Assert.Contains(p.Issues, i => i.Code == ParseIssueCode.InvalidTelemetry));
        Assert.True(broken.Count * 100 < reports.Count, $"{broken.Count} of {reports.Count} telemetry reports failed");
        Assert.DoesNotContain(reports, p => p.Payload is not null && p.Issues.Count > 0);
        Assert.Contains(reports, p => p.Payload is TelemetryReport { Digital.Count: 0 });
        Assert.Contains(reports, p => p.Payload is TelemetryReport { Digital.Count: 8 });
    }

    [Fact]
    public void MostWeatherStationsReportWeather()
    {
        var stations = CorpusFiles()
            .SelectMany(row => Packets(row.Data))
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Payload is PositionedPayload { Symbol.Code: '_' })
            .Select(p => (PositionedPayload)p.Payload!)
            .ToList();

        // Some stations use the weather symbol with only a description in the comment.
        Assert.True(stations.Count > 900, $"only {stations.Count} weather station positions in the corpus");
        Assert.True(stations.Count(s => s.Weather is not null) > 900);
        Assert.Contains(stations, s => s.Weather is { PressureMillibars: > 900, TemperatureF: not null });
        Assert.Contains(stations, s => s.Weather is { LuminosityWattsPerSquareMeter: not null });
        Assert.DoesNotContain(stations, s => s.Weather is { HumidityPercent: > 100 or < 1 });
    }

    [Fact]
    public void AnIGateThatInsertsUiMarkersLeavesThePacketTypeUnknown()
    {
        // One IGate in the Kansas City capture inserts " <UI>:" before the data type identifier. The packet is kept
        // as sent; recognising the position behind the marker is a decision for the payload decoders.
        var packets = Packets("aprs-is/2026-10-12-r20km.log")
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Information.Span.StartsWith(" <UI>:"u8) || p.Information.Span.StartsWith(" <<UI>>:"u8))
            .ToList();

        Assert.NotEmpty(packets);
        Assert.All(packets, p => Assert.Equal(PacketType.Unknown, p.Type));
    }

    /// <summary>The packet bytes of each line, after the "&lt;timestamp&gt; " prefix.</summary>
    private static IEnumerable<(int LineNumber, byte[] Bytes)> Packets(string file)
    {
        var lines = File.ReadAllBytes(Path.Combine(_directory, file)).AsMemory();
        var lineNumber = 0;
        while (!lines.IsEmpty)
        {
            var newline = lines.Span.IndexOf((byte)'\n');
            var line = newline < 0 ? lines : lines[..newline];
            lines = newline < 0 ? default : lines[(newline + 1)..];
            lineNumber++;
            if (!line.IsEmpty)
            {
                yield return (lineNumber, line[(line.Span.IndexOf((byte)' ') + 1)..].ToArray());
            }
        }
    }
}
