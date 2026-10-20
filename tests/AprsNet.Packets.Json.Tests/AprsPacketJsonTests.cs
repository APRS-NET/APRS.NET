using System.Text;
using System.Text.Json;

namespace AprsNet.Packets.Json.Tests;

public sealed class AprsPacketJsonTests
{
    private static readonly DateTimeOffset Received = new(2026, 10, 12, 23, 0, 0, 123, TimeSpan.Zero);

    // One packet of each type the format describes, in golden.jsonl's order.
    private static readonly string[] Examples =
    [
        "N0CALL-9>APRS,WIDE1-1,qAR,N1ABC-10:@122256z4903.50N/07201.75W>088/036/A=001234Mobile",
        "N0CALL-9>APRS:!4903.50N/07201.75W_220/004g005t077r000p000P000h50b10132",
        "N0CALL-9>APRS:=/5L!!<*e7>{?!Compressed PHG",
        @"QA0ACP>S9UTVY,qAO,Q0AAJ-1:`q[+mRQj/]""EV}146.520=",
        "N0CALL>APRS:;LEADER   *092345z4903.50N/07201.75W>088/036",
        "N0CALL>APRS:)AID #2!4903.50N/07201.75W/",
        "N0CALL>APRS:>092345zNet Control Center",
        "N0CALL>APRS::N1ABC    :Hello{001",
        "N0QBF>APRS::N0QBF-11 :EQNS.0,5.2,0,0,.53,-32,3,4.39,49,-32,3,18,1,2,3",
        "N0QBF-11>APRS:T#005,199,000,255,073,123,01101001",
        "N0CALL>APRS:_10090556c220s004g005t077r000p000P000h50b09900wRSW",
        "N0CALL>APRS,TCPIP*:}N1ABC>APRS,TCPIP,N0CALL*:>Third party",
        "N0CALL>APRS:<IGATE,MSG_CNT=0,LOC_CNT=1",
        "N0CALL>APRS:{Q1hello",
        "N0CALL>APRS:!!006B005803500000----03E9--------002105140000005D",
        "N0CALL>APRS:$GPRMC,063909,A,3349.4302,N,11700.3721,W,43.022,89.3,291099,13.6,E*52",
        "N0CALL>APRS:not a packet type",
    ];

    [Fact]
    public void EachTypeMatchesTheGoldenFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden.jsonl");
        var actual = Examples.Select(line => AprsPacketJson.Serialize(AprsPacket.Parse(line), Received)).ToArray();
        if (Environment.GetEnvironmentVariable("APRSNET_UPDATE_GOLDEN") is { } source)
        {
            // Run with APRSNET_UPDATE_GOLDEN=<path to tests/AprsNet.Packets.Json.Tests/golden.jsonl> to rewrite it.
            File.WriteAllLines(source, actual);
        }

        Assert.Equal(File.ReadAllLines(path), actual);
    }

    [Fact]
    public void ThePacketsAreValidJsonObjects()
    {
        foreach (var line in Examples)
        {
            using var document = JsonDocument.Parse(AprsPacketJson.Serialize(AprsPacket.Parse(line), Received));
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            Assert.Equal(line, document.RootElement.GetProperty("raw").GetString());
            Assert.Equal("2026-10-12T23:00:00.123Z", document.RootElement.GetProperty("received").GetString());
        }
    }

    [Fact]
    public void TextIsWrittenAsIs()
    {
        var json = AprsPacketJson.Serialize(AprsPacket.Parse("N0CALL>APRS:>21°C café"), Received);
        Assert.Contains("\"text\":\"21°C café\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PacketsCanBeWrittenIntoALargerDocument()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, AprsPacketJson.WriterOptions))
        {
            json.WriteStartArray();
            AprsPacketJson.Write(json, AprsPacket.Parse(Examples[0]), Received);
            AprsPacketJson.Write(json, AprsPacket.Parse(Examples[6]), Received);
            json.WriteEndArray();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        Assert.Equal(
            ["position", "status"],
            document.RootElement.EnumerateArray().Select(p => p.GetProperty("type").GetString())
        );
    }

    [Fact]
    public void EveryCorpusPacketIsWritten()
    {
        var written = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "corpus"), "*.log"))
        {
            foreach (var line in File.ReadLines(file, Encoding.Latin1))
            {
                var space = line.IndexOf(' ', StringComparison.Ordinal);
                var packet = AprsPacket.Parse(Encoding.Latin1.GetBytes(line[(space + 1)..]));
                using var document = JsonDocument.Parse(AprsPacketJson.Serialize(packet, Received));
                Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
                written++;
            }
        }

        Assert.True(written > 50_000, $"only {written} corpus packets");
    }
}
