using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Gateway.Tests;

public sealed class GatewayMetricsTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // Instruments are shared by every server in the process, so each test reads only its own server's measurements.
    private readonly string _name = "M" + Random.Shared.Next(10_000_000, 99_999_999);
    private readonly ConcurrentQueue<(string Instrument, long Value, Dictionary<string, string?> Tags)> _seen = new();
    private readonly MeterListener _listener = new();

    public GatewayMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == GatewayMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) =>
            {
                var map = new Dictionary<string, string?>();
                foreach (var tag in tags)
                {
                    map[tag.Key] = tag.Value?.ToString();
                }

                if (map.GetValueOrDefault("server") == _name)
                {
                    _seen.Enqueue((instrument.Name, value, map));
                }
            }
        );
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    private long Sum(string instrument, string? tag = null, string? value = null) =>
        _seen
            .Where(m => m.Instrument == instrument && (tag is null || m.Tags.GetValueOrDefault(tag) == value))
            .Sum(m => m.Value);

    [Fact]
    public async Task AClientsVisitIsCounted()
    {
        await using var server = Start(maxClients: 1);
        using (var client = await LogInAsync(server, "user N0CALL pass 13023 filter p/N1"))
        {
            server.Publish(AprsPacket.Parse("N1ABC>APRS:>one"));
            server.Publish(AprsPacket.Parse("N2ABC>APRS:>two"));
            Assert.Equal(
                "N1ABC>APRS:>one",
                await client
                    .ReadLineAsync(TestContext.Current.CancellationToken)
                    .AsTask()
                    .WaitAsync(Wait, TestContext.Current.CancellationToken)
            );
            Assert.Equal(1, Sum("aprsnet.gateway.clients.connected"));

            using var refused = new TcpClient();
            await refused.ConnectAsync(server.LocalEndPoint!, TestContext.Current.CancellationToken);
            await WaitUntil(() => Sum("aprsnet.gateway.clients.disconnected", "reason", "server_full") == 1);
        }

        await WaitUntil(() => Sum("aprsnet.gateway.clients.disconnected", "reason", "closed") == 1);
        Assert.Equal(0, Sum("aprsnet.gateway.clients.connected"));
        Assert.Equal(1, Sum("aprsnet.gateway.logins", "verified", "true"));
        Assert.Equal(2, Sum("aprsnet.gateway.packets.published"));
        Assert.Equal(1, Sum("aprsnet.gateway.packets.queued"));
    }

    [Fact]
    public async Task DisconnectReasonsAreTagged()
    {
        var time = new FakeTimeProvider();
        await using var server = Start(time: time);

        using (await LogInAsync(server, "nonsense", expectResponse: false))
        {
            await WaitUntil(() => Sum("aprsnet.gateway.clients.disconnected", "reason", "invalid_login") == 1);
        }

        using var silent = new TcpClient();
        await silent.ConnectAsync(server.LocalEndPoint!, TestContext.Current.CancellationToken);
        await WaitUntil(() => Sum("aprsnet.gateway.clients.connected") == 1);
        while (Sum("aprsnet.gateway.clients.disconnected", "reason", "login_timeout") == 0)
        {
            time.Advance(TimeSpan.FromSeconds(31));
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        using var unverified = await LogInAsync(server, "user N0CALL pass -1");
        await server.DisposeAsync();
        await WaitUntil(() => Sum("aprsnet.gateway.clients.disconnected", "reason", "server_stopping") == 1);
        Assert.Equal(1, Sum("aprsnet.gateway.logins", "verified", "false"));
        Assert.Equal(0, Sum("aprsnet.gateway.clients.connected"));
    }

    [Fact]
    public async Task AFeedCountsPacketsAndDuplicatesBySource()
    {
        await using var server = Start();
        await using var feed = new GatewayFeed(server);
        var aprsIs = Channel.CreateUnbounded<AprsPacket>();
        var tnc = Channel.CreateUnbounded<AprsPacket>();
        _ = feed.AddSource("aprs-is", aprsIs.Reader.ReadAllAsync(TestContext.Current.CancellationToken));
        _ = feed.AddSource("tnc", tnc.Reader.ReadAllAsync(TestContext.Current.CancellationToken));

        aprsIs.Writer.TryWrite(AprsPacket.Parse("N1ABC>APRS,qAR,IGATE:>hello"));
        await WaitUntil(() => feed.Published == 1);
        tnc.Writer.TryWrite(AprsPacket.Parse("N1ABC>APRS:>hello"));
        tnc.Writer.TryWrite(AprsPacket.Parse("N2ABC>APRS:>rf only"));
        await WaitUntil(() => feed.Published == 2 && feed.Duplicates == 1);

        Assert.Equal(1, Sum("aprsnet.gateway.packets.received", "source", "aprs-is"));
        Assert.Equal(2, Sum("aprsnet.gateway.packets.received", "source", "tnc"));
        Assert.Equal(
            1,
            _seen
                .Where(m =>
                    m.Instrument == "aprsnet.gateway.packets.duplicates"
                    && m.Tags.GetValueOrDefault("server") == _name
                    && m.Tags.GetValueOrDefault("source") == "tnc"
                )
                .Sum(m => m.Value)
        );
        Assert.Equal(2, Sum("aprsnet.gateway.packets.published"));
    }

    private AprsIsServer Start(int maxClients = 10, TimeProvider? time = null)
    {
        var server = new AprsIsServer(
            new AprsIsServerOptions
            {
                ServerName = _name,
                EndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                MaxClients = maxClients,
                TimeProvider = time ?? new FakeTimeProvider(),
            }
        );
        server.Start();
        return server;
    }

    private static async Task<StreamReader> LogInAsync(AprsIsServer server, string login, bool expectResponse = true)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(server.LocalEndPoint!);
        var reader = new StreamReader(tcp.GetStream(), Encoding.Latin1);
        await reader.ReadLineAsync().WaitAsync(Wait);
        await tcp.GetStream().WriteAsync(Encoding.Latin1.GetBytes(login + "\r\n"));
        if (expectResponse)
        {
            Assert.StartsWith("# logresp", await reader.ReadLineAsync().WaitAsync(Wait));
            if (login.Contains("filter", StringComparison.Ordinal))
            {
                await reader.ReadLineAsync().WaitAsync(Wait);
            }
        }

        return reader;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            await Task.Delay(10);
        }
    }
}
