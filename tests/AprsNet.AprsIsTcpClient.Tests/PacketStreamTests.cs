using System.Diagnostics.Metrics;
using AprsNet.Packets;

namespace AprsNet.AprsIsTcpClient.Tests;

public class PacketStreamTests
{
    private static AprsIsClientOptions Options() =>
        new()
        {
            Callsign = "NZ0P",
            SoftwareName = "test",
            SoftwareVersion = "1.0",
        };

    private static async Task<List<AprsPacket>> Take(IAsyncEnumerable<AprsPacket> packets, int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var taken = new List<AprsPacket>();
        await foreach (var packet in packets.WithCancellation(cts.Token))
        {
            taken.Add(packet);
            if (taken.Count == count)
            {
                break;
            }
        }

        return taken;
    }

    [Fact]
    public async Task TheClientYieldsParsedPacketsAndSkipsServerComments()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        transport.Server("W1AW>APRS:>Status text");
        transport.Server("# aprsc keepalive");
        transport.Server("W1AW>APRS:!9903.50N/07201.75W>");
        transport.Server("W1AW>APRS:`(_fn\"Oj/]"); // a Mic-E body without a Mic-E destination

        var packets = await Take(client.ReadPacketsAsync(TestContext.Current.CancellationToken), 3);

        Assert.Equal("Status text", ((StatusReport)packets[0].Payload!).Text);
        Assert.Equal(ParseOutcome.Malformed, packets[1].Outcome);
        Assert.Contains(packets[1].Issues, i => i.Code == ParseIssueCode.InvalidLatitude);
        Assert.Equal(PacketType.MicE, packets[2].Type);
    }

    [Fact]
    public async Task KeepsEveryByteOfANonAsciiLine()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        transport.Server("W1AW>APRS:>Grüße"); // Latin-1 bytes FC DF, not valid UTF-8 on their own

        var packet = (await Take(client.ReadPacketsAsync(TestContext.Current.CancellationToken), 1))[0];

        Assert.Equal([0xFC, 0xDF], packet.Information.Span[^3..^1].ToArray());
        Assert.Equal("Grüße", ((StatusReport)packet.Payload!).Text);
    }

    [Fact]
    public async Task TheConnectionYieldsParsedPacketsAcrossReconnects()
    {
        var first = new FakeAprsTransport();
        first.Server("# logresp NZ0P unverified, server T2ONE");
        var second = new FakeAprsTransport();
        second.Server("# logresp NZ0P unverified, server T2TWO");
        var transports = new Queue<FakeAprsTransport>([first, second]);
        var options = Options();
        options.ReconnectDelay = TimeSpan.FromMilliseconds(10);
        await using var connection = new AprsIsConnection(options, transportFactory: transports.Dequeue);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        first.Server("W1AW>APRS:>one");
        first.Close();
        second.Server("W1AW>APRS:>two");

        var packets = await Take(connection.ReadPacketsAsync(TestContext.Current.CancellationToken), 2);

        Assert.Equal(["one", "two"], packets.Select(p => ((StatusReport)p.Payload!).Text));
    }

    [Fact]
    public async Task CountsEachPacketByTypeAndOutcome()
    {
        var counted = new List<(string Type, string Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "aprsnet.aprsis.packets.received")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, tags, _) =>
            {
                string? type = null;
                string? outcome = null;
                foreach (var tag in tags)
                {
                    (type, outcome) = tag.Key switch
                    {
                        "type" => ((string?)tag.Value, outcome),
                        "outcome" => (type, (string?)tag.Value),
                        _ => (type, outcome),
                    };
                }

                lock (counted)
                {
                    counted.Add((type!, outcome!));
                }
            }
        );
        listener.Start();

        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        transport.Server("TESTMIC>S32U6T:`(_fn\"Oj/]");
        transport.Server("TESTBAD>APRS:!bad position");
        await Take(client.ReadPacketsAsync(TestContext.Current.CancellationToken), 2);

        // Other tests may count packets at the same time, so look for this test's two.
        lock (counted)
        {
            Assert.Contains(("mic_e", "parsed"), counted);
            Assert.Contains(("position", "malformed"), counted);
        }
    }
}
