using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

public sealed class KissMetricsTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // Instruments are shared by every client in the process, so each test reads only its own TNC's measurements.
    private readonly string _host = "m" + Guid.NewGuid().ToString("N")[..8];
    private readonly ConcurrentQueue<(string Instrument, long Value, Dictionary<string, string?> Tags)> _seen = new();
    private readonly MeterListener _listener = new();
    private readonly FakeTimeProvider _time = new();

    public KissMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == KissMetrics.MeterName)
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

                // A server's tag is its address, known once it listens; Sum picks out this test's.
                if (
                    map.GetValueOrDefault("tnc")?.StartsWith(_host, StringComparison.Ordinal) == true
                    || map.ContainsKey("server")
                )
                {
                    _seen.Enqueue((instrument.Name, value, map));
                }
            }
        );
        _listener.Start();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Tnc => $"{_host}:8001";

    public void Dispose() => _listener.Dispose();

    private long Sum(string instrument, string? tag = null, string? value = null) =>
        _seen
            .Where(m => m.Instrument == instrument && (tag is null || m.Tags.GetValueOrDefault(tag) == value))
            .Sum(m => m.Value);

    [Fact]
    public async Task AConnectionCountsFramesConnectionsAndReconnects()
    {
        var first = new FakeTnc();
        var second = new FakeTnc();
        var tncs = new Queue<FakeTnc>([first, second]);
        await using var connection = new KissTcpConnection(
            _ => Task.FromResult<Stream>(tncs.Dequeue()),
            new KissTcpOptions
            {
                Host = _host,
                TimeProvider = _time,
                TransmitLimit = KissTransmitLimit.Off,
            }
        );
        first.Send(Kiss("N0CALL>APRS:>one"));
        first.Send([KissFrame.FrameEnd, 0x00, KissFrame.FrameEscape, 0x41, KissFrame.FrameEnd]);
        first.Send(Kiss("N0CALL>APRS:>two"));
        await using var packets = connection.ReadPacketsAsync(Token).GetAsyncEnumerator(Token);
        Assert.True(await packets.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        Assert.True(await packets.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        await connection.SendAsync(AprsPacket.Parse("N0CALL>APZNET:>sent"), Token);
        Assert.Equal(1, Sum("aprsnet.kiss.connection.active"));

        first.End();
        var next = packets.MoveNextAsync().AsTask();
        await WaitUntil(() => Sum("aprsnet.kiss.connection.reconnects", "cause", "tnc_closed") == 1);
        Assert.Equal(0, Sum("aprsnet.kiss.connection.active"));
        second.Send(Kiss("N0CALL>APRS:>three"));
        await AdvanceUntilDone(next);
        Assert.True(await next);

        Assert.Equal(3, Sum("aprsnet.kiss.frames.received", "tnc", Tnc));
        Assert.Equal(1, Sum("aprsnet.kiss.frames.sent", "tnc", Tnc));
        Assert.Equal(1, Sum("aprsnet.kiss.frames.dropped", "tnc", Tnc));
        Assert.Equal(1, Sum("aprsnet.kiss.connection.active"));
    }

    [Fact]
    public async Task AFailedConnectIsCountedAsSuch()
    {
        var attempts = 0;
        var tnc = new FakeTnc();
        await using var connection = new KissTcpConnection(
            _ => ++attempts == 1 ? throw new IOException("refused") : Task.FromResult<Stream>(tnc),
            new KissTcpOptions { Host = _host, TimeProvider = _time }
        );
        tnc.Send(Kiss("N0CALL>APRS:>at last"));
        await using var packets = connection.ReadPacketsAsync(Token).GetAsyncEnumerator(Token);
        var next = packets.MoveNextAsync().AsTask();
        await WaitUntil(() => connection.State == KissConnectionState.Reconnecting);

        Assert.Equal(1, Sum("aprsnet.kiss.connection.reconnects", "cause", "connect_failed"));
        await AdvanceUntilDone(next);
        Assert.True(await next);
    }

    [Fact]
    public async Task AServerCountsAppsAndFrames()
    {
        await using var server = new KissTcpServer(new IPEndPoint(IPAddress.Loopback, 0), maxClients: 1)
        {
            Transmit = (_, _) => throw new InvalidOperationException("The TNC is not connected."),
        };
        server.Start();
        var name = server.LocalEndPoint!.ToString();
        long Server(string instrument, string? tag = null, string? value = null) =>
            _seen
                .Where(m =>
                    m.Instrument == instrument
                    && m.Tags.GetValueOrDefault("server") == name
                    && (tag is null || m.Tags.GetValueOrDefault(tag) == value)
                )
                .Sum(m => m.Value);

        using var app = new TcpClient();
        await app.ConnectAsync(server.LocalEndPoint, Token);
        await WaitUntil(() => Server("aprsnet.kiss.server.clients.connected") == 1);

        // The second app finds the server full.
        using (var turnedAway = new TcpClient())
        {
            await turnedAway.ConnectAsync(server.LocalEndPoint, Token);
            await WaitUntil(() => Server("aprsnet.kiss.server.clients.disconnected", "reason", "server_full") == 1);
        }

        server.Publish(Ax25Frame.FromAprsPacket(AprsPacket.Parse("N1ABC>APRS:>heard")));
        await app.GetStream().WriteAsync(Kiss("N0CALL>APRS:>from an app"), Token);
        await WaitUntil(() => Server("aprsnet.kiss.server.frames.transmit_failures") == 1);

        app.Close();
        await WaitUntil(() => Server("aprsnet.kiss.server.clients.disconnected", "reason", "closed") == 1);
        Assert.Equal(0, Server("aprsnet.kiss.server.clients.connected"));
        Assert.Equal(1, Server("aprsnet.kiss.server.frames.published"));
        Assert.Equal(1, Server("aprsnet.kiss.server.frames.received"));
    }

    /// <summary>The reconnect wait starts just after the state changes, so keep advancing until it has ended.</summary>
    private async Task AdvanceUntilDone(Task task)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!task.IsCompleted)
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            _time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(20, Token);
        }
    }

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line))).Encode();

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
