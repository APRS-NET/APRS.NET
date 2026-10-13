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
    public void EveryMessageDecodes()
    {
        var messages = CorpusFiles()
            .SelectMany(row => Packets(row.Data))
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Type == PacketType.Message)
            .ToList();

        Assert.True(messages.Count > 300, $"only {messages.Count} messages in the corpus");
        Assert.All(messages, p => Assert.Equal(ParseOutcome.Parsed, p.Outcome));
        Assert.Contains(messages, p => p.Payload is TelemetryDefinition { Kind: TelemetryDefinitionKind.Equations });
        Assert.Contains(messages, p => p.Payload is MessagePayload { Kind: MessageKind.Acknowledgement });
        Assert.DoesNotContain(messages, p => p.Issues.Count > 0);
    }

    [Fact]
    public void EveryObjectAndItemDecodes()
    {
        var packets = CorpusFiles()
            .SelectMany(row => Packets(row.Data))
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Type is PacketType.Object or PacketType.Item)
            .ToList();

        Assert.True(packets.Count > 900, $"only {packets.Count} objects and items in the corpus");
        Assert.All(packets, p => Assert.IsAssignableFrom<PositionedPayload>(p.Payload));
        Assert.Contains(packets, p => p.Payload is ObjectReport { IsLive: false });
        Assert.Contains(packets, p => p.Payload is ItemReport);
    }

    [Fact]
    public void EveryMicEReportDecodes()
    {
        var reports = CorpusFiles()
            .SelectMany(row => Packets(row.Data))
            .Select(p => AprsPacket.Parse(p.Bytes))
            .Where(p => p.Type == PacketType.MicE)
            .ToList();

        Assert.True(reports.Count > 800, $"only {reports.Count} Mic-E reports in the corpus");
        Assert.All(reports, p => Assert.IsType<MicEReport>(p.Payload));
        Assert.DoesNotContain(reports, p => p.Issues.Count > 0);
        Assert.Contains(reports, p => p.Payload is MicEReport { Device: "TM-D710" });
        Assert.Contains(reports, p => p.Payload is MicEReport { Device: "FTM-400DR" });
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
