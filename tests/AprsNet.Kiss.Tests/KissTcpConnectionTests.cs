using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

public sealed class KissTcpConnectionTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly string[] TcpLines = ["N0CALL>APRS:>first connection", "N0CALL>APRS:>second connection"];
    private readonly FakeTimeProvider _time = new();

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)), 0).Encode();

    private KissTcpConnection Connection(Func<CancellationToken, Task<Stream>> connect) =>
        new(
            connect,
            new KissTcpOptions
            {
                ReconnectDelay = TimeSpan.FromSeconds(5),
                MaxReconnectDelay = TimeSpan.FromSeconds(15),
                TimeProvider = _time,
            }
        );

    [Fact]
    public async Task ReadingCarriesOnAfterTheTncClosesTheConnection()
    {
        var first = new FakeTnc();
        var second = new FakeTnc();
        var tncs = new Queue<FakeTnc>([first, second]);
        await using var connection = Connection(_ => Task.FromResult<Stream>(tncs.Dequeue()));
        var lost = new List<KissConnectionStateChangedEventArgs>();
        connection.StateChanged += (_, e) =>
        {
            if (e.State == KissConnectionState.Reconnecting)
            {
                lost.Add(e);
            }
        };

        first.Send(Kiss("N0CALL>APRS:>one"));
        first.End();
        second.Send(Kiss("N0CALL>APRS:>two"));

        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.Equal("N0CALL>APRS:>one", await NextAsync(packets));
        var next = NextAsync(packets);
        await WaitUntil(() => lost.Count == 1);
        Assert.Null(lost[0].Error);
        Assert.Equal(TimeSpan.FromSeconds(5), lost[0].RetryIn!.Value);
        Assert.False(connection.IsConnected);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("N0CALL>APRS:>two", await next);
        Assert.True(connection.IsConnected);
        Assert.Equal(1, connection.Reconnects);
    }

    [Fact]
    public async Task FailedAttemptsWaitLongerEachTimeUpToTheLimit()
    {
        var attempts = 0;
        var tnc = new FakeTnc();
        tnc.Send(Kiss("N0CALL>APRS:>at last"));
        await using var connection = Connection(_ =>
            ++attempts <= 4
                ? throw new SocketException((int)SocketError.ConnectionRefused)
                : Task.FromResult<Stream>(tnc)
        );
        var lost = new List<KissConnectionStateChangedEventArgs>();
        connection.StateChanged += (_, e) =>
        {
            if (e.State == KissConnectionState.Reconnecting)
            {
                lost.Add(e);
            }
        };

        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = NextAsync(packets);
        for (var i = 1; i <= 4; i++)
        {
            await WaitUntil(() => lost.Count == i);
            _time.Advance(lost[^1].RetryIn!.Value);
        }

        Assert.Equal("N0CALL>APRS:>at last", await next);
        Assert.All(lost, e => Assert.IsType<SocketException>(e.Error));
        Assert.Equal([5, 10, 15, 15], lost.Select(e => e.RetryIn!.Value.TotalSeconds));
        Assert.Equal(0, connection.Reconnects);
    }

    [Fact]
    public async Task AReadErrorIsReportedAndTheDelayStartsAgain()
    {
        var broken = new FakeTnc();
        var good = new FakeTnc();
        var tncs = new Queue<FakeTnc>([broken, good]);
        await using var connection = Connection(_ => Task.FromResult<Stream>(tncs.Dequeue()));
        var lost = new List<KissConnectionStateChangedEventArgs>();
        connection.StateChanged += (_, e) =>
        {
            if (e.State == KissConnectionState.Reconnecting)
            {
                lost.Add(e);
            }
        };
        broken.Fail(new IOException("link down"));
        good.Send(Kiss("N0CALL>APRS:>back"));

        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = NextAsync(packets);
        await WaitUntil(() => lost.Count == 1);
        Assert.Equal("link down", lost[0].Error!.Message);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("N0CALL>APRS:>back", await next);
    }

    [Fact]
    public async Task PacketsAreSentOnlyWhileConnected()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(_ => Task.FromResult<Stream>(tnc));
        var packet = AprsPacket.Parse("N0CALL>APRS:>hello");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.SendAsync(packet, TestContext.Current.CancellationToken).AsTask()
        );

        tnc.Send(Kiss("N0CALL>APRS:>connected"));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await NextAsync(packets);
        await connection.SendAsync(packet, TestContext.Current.CancellationToken);
        Assert.Equal(Kiss("N0CALL>APRS:>hello"), tnc.Written.ToArray());
    }

    [Fact]
    public async Task AConnectionKeepsOneLimitAcrossReconnects()
    {
        var first = new FakeTnc();
        var second = new FakeTnc();
        var tncs = new Queue<FakeTnc>([first, second]);
        var limit = new KissTransmitLimit(2, TimeSpan.FromSeconds(10), _time);
        await using var connection = new KissTcpConnection(
            _ => Task.FromResult<Stream>(tncs.Dequeue()),
            new KissTcpOptions
            {
                ReconnectDelay = TimeSpan.FromSeconds(5),
                MaxReconnectDelay = TimeSpan.FromSeconds(5),
                TimeProvider = _time,
                TransmitLimit = limit,
            }
        );
        var frame = Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL>APRS:>beacon"));
        first.Send(Kiss("N0CALL>APRS:>first"));
        second.Send(Kiss("N0CALL>APRS:>second"));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await packets.MoveNextAsync().AsTask().WaitAsync(Wait, TestContext.Current.CancellationToken));

        await connection.SendFrameAsync(frame, TestContext.Current.CancellationToken);
        await connection.SendFrameAsync(frame, TestContext.Current.CancellationToken);
        first.End();
        var next = packets.MoveNextAsync().AsTask();
        await WaitUntil(() => !connection.IsConnected);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await next.WaitAsync(Wait, TestContext.Current.CancellationToken));

        // The burst was used on the first connection; the new one does not start a fresh one.
        var third = connection.SendFrameAsync(frame, TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(third.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(5));
        await third.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Single(new KissDecoder().Decode(second.Written.ToArray()));
    }

    [Fact]
    public async Task ReportsEachStateChangeOnce()
    {
        var first = new FakeTnc();
        var second = new FakeTnc();
        var tncs = new Queue<FakeTnc>([first, second]);
        var connection = Connection(_ => Task.FromResult<Stream>(tncs.Dequeue()));
        var changes = new List<KissConnectionStateChangedEventArgs>();
        connection.StateChanged += (_, e) =>
        {
            lock (changes)
            {
                changes.Add(e);
            }
        };
        Assert.Equal(KissConnectionState.Idle, connection.State);

        first.Send(Kiss("N0CALL>APRS:>one"));
        using (var reading = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            await using var packets = connection.ReadPacketsAsync(reading.Token).GetAsyncEnumerator(reading.Token);
            Assert.Equal("N0CALL>APRS:>one", await NextAsync(packets));
            Assert.Equal(KissConnectionState.Connected, connection.State);
            first.End();
            var next = NextAsync(packets);
            await WaitUntil(() => connection.State == KissConnectionState.Reconnecting);
            second.Send(Kiss("N0CALL>APRS:>two"));
            // The state changes just before the wait starts, so keep advancing until the wait has seen it.
            while (!next.IsCompleted)
            {
                _time.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.Equal("N0CALL>APRS:>two", await next);
            await reading.CancelAsync();
        }

        await WaitUntil(() => connection.State == KissConnectionState.Idle);
        await connection.DisposeAsync();

        Assert.Equal(
            [
                KissConnectionState.Connecting,
                KissConnectionState.Connected,
                KissConnectionState.Reconnecting,
                KissConnectionState.Connecting,
                KissConnectionState.Connected,
                KissConnectionState.Idle,
                KissConnectionState.Closed,
            ],
            changes.Select(c => c.State)
        );
        var reconnecting = changes[2];
        Assert.Null(reconnecting.Error);
        Assert.Equal(TimeSpan.FromSeconds(5), reconnecting.RetryIn);
        Assert.All(changes.Where(c => c.State != KissConnectionState.Reconnecting), c => Assert.Null(c.RetryIn));
    }

    [Fact]
    public async Task DisposingEndsTheRead()
    {
        var connection = Connection(_ => Task.FromResult<Stream>(new FakeTnc()));
        await using var frames = connection
            .ReadFramesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = frames.MoveNextAsync().AsTask();
        await WaitUntil(() => connection.IsConnected);
        await connection.DisposeAsync();
        Assert.False(await next.WaitAsync(Wait, TestContext.Current.CancellationToken));
        await connection.DisposeAsync();
        var frame = Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL>APRS:>x"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            connection.SendFrameAsync(frame, TestContext.Current.CancellationToken).AsTask()
        );
    }

    [Fact]
    public async Task OnlyOneReadRunsAtATime()
    {
        await using var connection = Connection(_ => Task.FromResult<Stream>(new FakeTnc()));
        using var stop = new CancellationTokenSource();
        await using var first = connection.ReadFramesAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        var pending = first.MoveNextAsync().AsTask();
        await using var second = connection
            .ReadFramesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.MoveNextAsync().AsTask());
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pending.WaitAsync(Wait, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public void BadSettingsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new KissTcpConnection(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KissTcpConnection("tnc", port: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KissTcpConnection(new KissTcpOptions { TncPort = 16 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KissTcpConnection(new KissTcpOptions { ReconnectDelay = TimeSpan.Zero })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KissTcpConnection(
                new KissTcpOptions
                {
                    ReconnectDelay = TimeSpan.FromSeconds(10),
                    MaxReconnectDelay = TimeSpan.FromSeconds(5),
                }
            )
        );
        var connection = new KissTcpConnection("tnc");
        Assert.Equal(TimeSpan.FromSeconds(5), connection.ReconnectDelay);
        Assert.Equal(TimeSpan.FromSeconds(60), connection.MaxReconnectDelay);
    }

    [Fact]
    public async Task AReconnectingTcpTncIsReadAcrossConnections()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var token = TestContext.Current.CancellationToken;
        var tnc = Task.Run(
            async () =>
            {
                foreach (var line in TcpLines)
                {
                    using var client = await listener.AcceptTcpClientAsync(token);
                    await client.GetStream().WriteAsync(Kiss(line), token);
                    await Task.Delay(50, token);
                }
            },
            token
        );

        await using var connection = new KissTcpConnection(
            new KissTcpOptions
            {
                Host = "127.0.0.1",
                Port = port,
                ReconnectDelay = TimeSpan.FromMilliseconds(20),
            }
        );
        await using var packets = connection.ReadPacketsAsync(token).GetAsyncEnumerator(token);
        Assert.Equal("N0CALL>APRS:>first connection", await NextAsync(packets));
        Assert.Equal("N0CALL>APRS:>second connection", await NextAsync(packets));
        Assert.Equal(1, connection.Reconnects);
        await tnc;
    }

    [Fact]
    public async Task MergesWithAnAprsIsFeedAndReportsDuplicatesWithTheirSource()
    {
        var token = TestContext.Current.CancellationToken;
        var tnc = new FakeTnc();
        await using var connection = Connection(_ => Task.FromResult<Stream>(tnc));
        var aprsIs = Channel.CreateUnbounded<AprsPacket>();
        await using var merger = new PacketMerger(timeProvider: _time);
        _ = merger.Add("aprs-is", aprsIs.Reader.ReadAllAsync(token));
        _ = merger.Add("tnc", connection.ReadPacketsAsync(token));
        await using var merged = merger.ReadAllAsync(token).GetAsyncEnumerator(token);

        // Heard by the local TNC first, then the same packet arrives from APRS-IS through an IGate.
        tnc.Send(Kiss("N0CALL>APRS,WIDE1-1:>hello"));
        Assert.True(await merged.MoveNextAsync().AsTask().WaitAsync(Wait, token));
        var fromTnc = merged.Current;
        aprsIs.Writer.TryWrite(AprsPacket.Parse("N0CALL>APRS,WIDE1-1,qAR,IGATE:>hello"));
        Assert.True(await merged.MoveNextAsync().AsTask().WaitAsync(Wait, token));
        var fromAprsIs = merged.Current;

        Assert.Equal("tnc", fromTnc.Source);
        Assert.False(fromTnc.IsDuplicate);
        Assert.Equal("aprs-is", fromAprsIs.Source);
        Assert.Equal("tnc", fromAprsIs.DuplicateOf?.Source);
    }

    private static async Task<string> NextAsync(IAsyncEnumerator<AprsPacket> packets)
    {
        Assert.True(await packets.MoveNextAsync().AsTask().WaitAsync(Wait));
        return packets.Current.ToString();
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

    /// <summary>A TNC end of a link: bytes queued to send, an orderly close, or a failure.</summary>
}
