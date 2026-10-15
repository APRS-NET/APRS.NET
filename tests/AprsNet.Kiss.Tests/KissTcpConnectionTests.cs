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
        new(connect, 0, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), _time);

    [Fact]
    public async Task ReadingCarriesOnAfterTheTncClosesTheConnection()
    {
        var first = new FakeTnc();
        var second = new FakeTnc();
        var tncs = new Queue<FakeTnc>([first, second]);
        await using var connection = Connection(_ => Task.FromResult<Stream>(tncs.Dequeue()));
        var lost = new List<KissConnectionLostEventArgs>();
        connection.ConnectionLost += (_, e) => lost.Add(e);

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
        Assert.Equal(TimeSpan.FromSeconds(5), lost[0].RetryIn);
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
        var lost = new List<KissConnectionLostEventArgs>();
        connection.ConnectionLost += (_, e) => lost.Add(e);

        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = NextAsync(packets);
        for (var i = 1; i <= 4; i++)
        {
            await WaitUntil(() => lost.Count == i);
            _time.Advance(lost[^1].RetryIn);
        }

        Assert.Equal("N0CALL>APRS:>at last", await next);
        Assert.All(lost, e => Assert.IsType<SocketException>(e.Error));
        Assert.Equal([5, 10, 15, 15], lost.Select(e => e.RetryIn.TotalSeconds));
        Assert.Equal(0, connection.Reconnects);
    }

    [Fact]
    public async Task AReadErrorIsReportedAndTheDelayStartsAgain()
    {
        var broken = new FakeTnc();
        var good = new FakeTnc();
        var tncs = new Queue<FakeTnc>([broken, good]);
        await using var connection = Connection(_ => Task.FromResult<Stream>(tncs.Dequeue()));
        var lost = new List<KissConnectionLostEventArgs>();
        connection.ConnectionLost += (_, e) => lost.Add(e);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new KissTcpConnection("tnc", tncPort: 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KissTcpConnection("tnc", reconnectDelay: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KissTcpConnection(
                "tnc",
                reconnectDelay: TimeSpan.FromSeconds(10),
                maxReconnectDelay: TimeSpan.FromSeconds(5)
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
            "127.0.0.1",
            port,
            reconnectDelay: TimeSpan.FromMilliseconds(20)
        );
        await using var packets = connection.ReadPacketsAsync(token).GetAsyncEnumerator(token);
        Assert.Equal("N0CALL>APRS:>first connection", await NextAsync(packets));
        Assert.Equal("N0CALL>APRS:>second connection", await NextAsync(packets));
        Assert.Equal(1, connection.Reconnects);
        await tnc;
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
    private sealed class FakeTnc : Stream
    {
        private readonly Channel<object> _incoming = Channel.CreateUnbounded<object>();

        public MemoryStream Written { get; } = new();

        public void Send(byte[] bytes) => _incoming.Writer.TryWrite(bytes);

        public void End() => _incoming.Writer.TryComplete();

        public void Fail(Exception error) => _incoming.Writer.TryWrite(error);

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (!await _incoming.Reader.WaitToReadAsync(cancellationToken))
            {
                return 0;
            }

            var next = await _incoming.Reader.ReadAsync(cancellationToken);
            if (next is Exception error)
            {
                throw error;
            }

            var bytes = (byte[])next;
            bytes.CopyTo(buffer);
            return bytes.Length;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
