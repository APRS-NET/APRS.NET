using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using AprsNet.AprsIsTcpClient;
using AprsNet.AprsIsTcpClient.Tests;
using AprsNet.Ax25;
using AprsNet.Kiss;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Gateway.Tests;

public sealed class GatewayFeedTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 7, 30, 0, TimeSpan.Zero));
    private AprsIsServer _server = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _server = new AprsIsServer(
            new AprsIsServerOptions
            {
                ServerName = "T2FEED",
                EndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                TimeProvider = _time,
            }
        );
        _server.Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task APacketHeardOnAprsIsAndATncIsServedOnce()
    {
        await using var feed = new GatewayFeed(_server);
        using var client = await LoggedInAsync("p/N");

        // APRS-IS: an IGate heard the packet and passed it on.
        var transport = new FakeAprsTransport();
        transport.Server("# aprsc 2.1.test");
        transport.Server("# logresp N0CALL unverified, server T2TEST");
        await using var aprsIs = new AprsIsConnection(
            new AprsIsClientOptions { Callsign = "N0CALL", Filter = "r/39/-95/100" },
            transportFactory: () => transport
        );
        await aprsIs.StartAsync(Token);
        _ = feed.AddSource("aprs-is", aprsIs.ReadPacketsAsync(Token));
        transport.Server("N1ABC>APRS,WIDE1-1,qAR,IGATE:>hello");
        await WaitUntil(() => feed.Published == 1);

        // The local TNC heard the same packet off the air.
        var tnc = new KissStreamClient(new MemoryStream(Kiss("N1ABC>APRS,WIDE1-1:>hello", "N1ABC>APRS:>only on RF")));
        await using (tnc)
        {
            await feed.AddSource("tnc", tnc.ReadPacketsAsync(Token)).WaitAsync(Wait, Token);
        }

        await WaitUntil(() => feed.Published == 2);
        Assert.Equal(
            "N1ABC>APRS,WIDE1-1,qAR,IGATE:>hello",
            await client.ReadLineAsync(Token).AsTask().WaitAsync(Wait, Token)
        );
        Assert.Equal("N1ABC>APRS:>only on RF", await client.ReadLineAsync(Token).AsTask().WaitAsync(Wait, Token));
        Assert.Equal(1, feed.Duplicates);
        var counts = feed.GetSourceCounts();
        Assert.Equal(new GatewaySourceCounts(1, 0), counts["aprs-is"]);
        Assert.Equal(new GatewaySourceCounts(2, 1), counts["tnc"]);
        Assert.Equal(1, counts["tnc"].Published);
        Assert.Equal(["aprs-is"], feed.Sources);
    }

    [Fact]
    public async Task OutsideTheWindowAPacketIsServedAgain()
    {
        await using var feed = new GatewayFeed(_server, TimeSpan.FromSeconds(5));
        var source = Channel.CreateUnbounded<AprsPacket>();
        _ = feed.AddSource("tnc", source.Reader.ReadAllAsync(Token));

        source.Writer.TryWrite(AprsPacket.Parse("N1ABC>APRS:>beacon"));
        await WaitUntil(() => feed.Published == 1);
        _time.Advance(TimeSpan.FromSeconds(5));
        source.Writer.TryWrite(AprsPacket.Parse("N1ABC>APRS:>beacon"));
        await WaitUntil(() => feed.Published == 2);

        Assert.Equal(0, feed.Duplicates);
    }

    [Fact]
    public async Task AFailingSourceEndsAloneAndKeepsItsCounts()
    {
        await using var feed = new GatewayFeed(_server);
        var healthy = Channel.CreateUnbounded<AprsPacket>();
        var broken = Channel.CreateUnbounded<AprsPacket>();
        _ = feed.AddSource("healthy", healthy.Reader.ReadAllAsync(Token));
        var failing = feed.AddSource("broken", broken.Reader.ReadAllAsync(Token));

        broken.Writer.TryWrite(AprsPacket.Parse("N1ABC>APRS:>before the fault"));
        await WaitUntil(() => feed.Published == 1);
        broken.Writer.TryComplete(new IOException("serial port gone"));
        await Assert.ThrowsAsync<IOException>(() => failing.WaitAsync(Wait, Token));
        healthy.Writer.TryWrite(AprsPacket.Parse("N2ABC>APRS:>still here"));
        await WaitUntil(() => feed.Published == 2);

        Assert.Equal(["healthy"], feed.Sources);
        Assert.Equal(new GatewaySourceCounts(1, 0), feed.GetSourceCounts()["broken"]);
        Assert.True(await feed.RemoveSourceAsync("healthy"));
        Assert.Empty(feed.Sources);
        Assert.False(await feed.RemoveSourceAsync("healthy"));
    }

    [Fact]
    public async Task DisposingStopsTheSourcesButNotTheServer()
    {
        var feed = new GatewayFeed(_server);
        var source = Channel.CreateUnbounded<AprsPacket>();
        var reading = feed.AddSource("tnc", source.Reader.ReadAllAsync(Token));

        await feed.DisposeAsync();
        await reading.WaitAsync(Wait, Token);
        await feed.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = feed.AddSource("late", source.Reader.ReadAllAsync(Token));
        });
        using var client = await LoggedInAsync("p/N");
        _server.Publish(AprsPacket.Parse("N1ABC>APRS:>server still up"));
        Assert.Equal("N1ABC>APRS:>server still up", await client.ReadLineAsync(Token).AsTask().WaitAsync(Wait, Token));
    }

    private static byte[] Kiss(params string[] lines) =>
        [.. lines.SelectMany(l => KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(l))).Encode())];

    private async Task<StreamReader> LoggedInAsync(string filter)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(_server.LocalEndPoint!, Token);
        var reader = new StreamReader(tcp.GetStream(), Encoding.Latin1);
        await reader.ReadLineAsync(Token);
        await tcp.GetStream()
            .WriteAsync(Encoding.Latin1.GetBytes($"user N0CALL pass -1 vers test 1.0 filter {filter}\r\n"), Token);
        Assert.StartsWith("# logresp", await reader.ReadLineAsync(Token));
        Assert.Equal($"# filter {filter} active", await reader.ReadLineAsync(Token));
        return reader;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            await Task.Delay(10, Token);
        }
    }
}
