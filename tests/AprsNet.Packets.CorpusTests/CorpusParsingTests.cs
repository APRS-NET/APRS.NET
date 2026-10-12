using System.Text;

namespace AprsNet.Packets.CorpusTests;

/// <summary>
/// Runs every captured packet through <see cref="AprsPacket.Parse(ReadOnlySpan{byte})"/>: real traffic must parse
/// without throwing, keep every byte, and have a well-formed header.
/// </summary>
public class CorpusParsingTests
{
    private static readonly string _directory = Path.Combine(AppContext.BaseDirectory, "corpus");

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
            Assert.DoesNotContain(packet.Issues, i => i.Severity == ParseIssueSeverity.Error);
        }
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
