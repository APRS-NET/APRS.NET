using System.Net;
using System.Net.Sockets;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

public sealed class KissTcpServerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static IPEndPoint AnyPort => new(IPAddress.Loopback, 0);

    private static Ax25Frame Frame(string line) => Ax25Frame.FromAprsPacket(AprsPacket.Parse(line));

    private static byte[] Kiss(string line) => KissFrame.ForAx25(Frame(line)).Encode();

    [Fact]
    public async Task AppsShareATnc()
    {
        var tnc = new FakeTnc();
        await using var connection = new KissTcpConnection(
            _ => Task.FromResult<Stream>(tnc),
            0,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            new FakeTimeProvider()
        );
        await using var server = new KissTcpServer(AnyPort) { Transmit = connection.SendFrameAsync };
        server.Start();
        var relaying = Task.Run(
            async () =>
            {
                await foreach (var frame in connection.ReadFramesAsync(Token))
                {
                    server.Publish(frame);
                }
            },
            Token
        );
        await WaitUntil(() => connection.IsConnected);
        await using var first = await App.ConnectAsync(server);
        await using var second = await App.ConnectAsync(server);
        await WaitUntil(() => server.ClientCount == 2);

        tnc.Send(Kiss("N1ABC>APRS,WIDE1-1:>heard on RF"));
        Assert.Equal("N1ABC>APRS,WIDE1-1:>heard on RF", await first.ReadAsync());
        Assert.Equal("N1ABC>APRS,WIDE1-1:>heard on RF", await second.ReadAsync());

        await first.SendAsync(Kiss("N0CALL>APZNET,WIDE1-1:>from the first app"));
        await WaitUntil(() => tnc.Written.Length > 0);
        await Task.Delay(100, Token);
        var sent = Assert.Single(new KissDecoder().Decode(tnc.Written.ToArray()));
        Assert.True(sent.TryGetAx25(out var frame));
        Assert.Equal("N0CALL>APZNET,WIDE1-1:>from the first app", frame!.ToTnc2());
        Assert.Equal(1, server.FramesFromClients);
        Assert.Equal(0, server.TransmitFailures);
        Assert.False(second.HasData, "a frame one app sends is not echoed to the others");
    }

    [Fact]
    public async Task AFrameTheTncCannotTakeIsCountedAndDropped()
    {
        await using var connection = new KissTcpConnection("127.0.0.1", 1);
        await using var server = new KissTcpServer(AnyPort) { Transmit = connection.SendFrameAsync };
        server.Start();
        await using var app = await App.ConnectAsync(server);

        await app.SendAsync(Kiss("N0CALL>APZNET:>nowhere to go"));
        await WaitUntil(() => server.TransmitFailures == 1);

        Assert.Equal(1, server.FramesFromClients);
        Assert.Equal(1, server.ClientCount);
    }

    [Fact]
    public async Task OnlyDataFramesAreTakenFromApps()
    {
        var received = new List<string>();
        await using var server = new KissTcpServer(AnyPort)
        {
            Transmit = (frame, _) =>
            {
                lock (received)
                {
                    received.Add(frame.ToTnc2());
                }

                return ValueTask.CompletedTask;
            },
        };
        server.Start();
        await using var app = await App.ConnectAsync(server);

        await app.SendAsync(new KissFrame(0, KissCommand.TxDelay, new byte[] { 50 }).Encode());
        await app.SendAsync(new KissFrame(0, KissCommand.Data, new byte[] { 1, 2, 3 }).Encode());
        await app.SendAsync(KissFrame.ForAx25(Frame("N0CALL>APZNET:>on port 3"), 3).Encode());
        await WaitUntil(() =>
        {
            lock (received)
            {
                return received.Count == 1;
            }
        });

        Assert.Equal(["N0CALL>APZNET:>on port 3"], received);
        Assert.Equal(1, server.FramesFromClients);
    }

    [Fact]
    public async Task AprsIsTrafficGoesToAppsAsThirdPartyFrames()
    {
        await using var server = new KissTcpServer(AnyPort);
        server.Start();
        await using var app = await App.ConnectAsync(server);
        await WaitUntil(() => server.ClientCount == 1);
        var gateway = PacketAddress.Parse("N0GATE-10", "APZNET", ["WIDE1-1"]);

        server.PublishFromAprsIs(AprsPacket.Parse("N1ABC-9>APRS,TCPIP*,qAC,T2TEST:>hello"), gateway);

        var line = await app.ReadAsync();
        Assert.Equal("N0GATE-10>APZNET:}N1ABC-9>APRS,TCPIP,N0GATE-10*:>hello", line);
        var inner = Assert.IsType<ThirdPartyPayload>(AprsPacket.Parse(line!).Payload).Packet;
        Assert.Equal("hello", Assert.IsType<StatusReport>(inner.Payload).Text);
        Assert.Throws<ArgumentException>(() => server.PublishFromAprsIs(AprsPacket.Parse(">no header"), gateway));
    }

    [Fact]
    public async Task AnAppThatFallsBehindIsDisconnected()
    {
        await using var server = new KissTcpServer(AnyPort, clientQueueLength: 4);
        server.Start();
        await using var slow = await App.ConnectAsync(server);
        await WaitUntil(() => server.ClientCount == 1);
        var frame = Frame("N0CALL>APRS:>" + new string('x', 200));

        for (var i = 0; i < 100_000 && server.ClientsDroppedForOverflow == 0; i++)
        {
            server.Publish(frame);
        }

        Assert.Equal(1, server.ClientsDroppedForOverflow);
        await WaitUntil(() => server.ClientCount == 0);
    }

    [Fact]
    public async Task RefusesAppsOverTheLimitAndDisconnectsAllWhenDisposed()
    {
        var server = new KissTcpServer(AnyPort, maxClients: 1);
        server.Start();
        await using var first = await App.ConnectAsync(server);
        await WaitUntil(() => server.ClientCount == 1);
        await using var second = await App.ConnectAsync(server);

        Assert.True(await second.ClosedAsync());
        Assert.Equal(1, server.ClientCount);
        await server.DisposeAsync();
        Assert.True(await first.ClosedAsync());
        await server.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(server.Start);
    }

    [Fact]
    public void RefusesBadSettings()
    {
        Assert.Throws<ArgumentNullException>(() => new KissTcpServer(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KissTcpServer(AnyPort, maxClients: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KissTcpServer(AnyPort, clientQueueLength: 0));
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

    /// <summary>A KISS app connected to the server.</summary>
    private sealed class App : IAsyncDisposable
    {
        private readonly TcpClient _tcp;
        private readonly KissDecoder _decoder = new();
        private readonly Queue<KissFrame> _frames = new();
        private readonly byte[] _buffer = new byte[4096];

        private App(TcpClient tcp) => _tcp = tcp;

        public bool HasData => _tcp.Available > 0;

        public static async Task<App> ConnectAsync(KissTcpServer server)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(server.LocalEndPoint!, Token);
            return new App(tcp);
        }

        public async Task<string?> ReadAsync()
        {
            while (_frames.Count == 0)
            {
                var read = await _tcp.GetStream().ReadAsync(_buffer, Token).AsTask().WaitAsync(Wait, Token);
                if (read == 0)
                {
                    return null;
                }

                foreach (var frame in _decoder.Decode(_buffer.AsSpan(0, read)))
                {
                    _frames.Enqueue(frame);
                }
            }

            Assert.True(_frames.Dequeue().TryGetAx25(out var ax25));
            return ax25!.ToTnc2();
        }

        public async Task SendAsync(byte[] bytes) => await _tcp.GetStream().WriteAsync(bytes, Token);

        public async Task<bool> ClosedAsync()
        {
            try
            {
                return await _tcp.GetStream().ReadAsync(_buffer, Token).AsTask().WaitAsync(Wait, Token) == 0;
            }
            catch (IOException)
            {
                return true;
            }
        }

        public ValueTask DisposeAsync()
        {
            _tcp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
