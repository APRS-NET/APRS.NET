using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss.Tests;

public class KissStreamClientTests
{
    /// <summary>A serial port stand-in: reads come from what the TNC "sent", writes are collected.</summary>
    private sealed class FakeSerialPort(byte[] fromTnc) : Stream
    {
        private readonly MemoryStream _in = new(fromTnc);

        public MemoryStream Written { get; } = new();

        public bool Disposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        // One byte at a time, as a slow serial line delivers it.
        public override int Read(byte[] buffer, int offset, int count) => _in.Read(buffer, offset, Math.Min(count, 1));

        /// <summary>Each write as it came, to tell whether a frame went to the port whole.</summary>
        public List<byte[]> Writes { get; } = [];

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (Writes)
            {
                Writes.Add(buffer.AsSpan(offset, count).ToArray());
                Written.Write(buffer, offset, count);
            }
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static byte[] Wire(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line))).Encode();

    [Fact]
    public async Task ReadsPacketsArrivingOneByteAtATime()
    {
        await using var port = new FakeSerialPort([.. Wire("N0CALL>APRS:>One"), .. Wire("N0CALL>APRS:>Two")]);
        await using var client = new KissStreamClient(port);

        var packets = new List<string>();
        await foreach (var packet in client.ReadPacketsAsync(TestContext.Current.CancellationToken))
        {
            packets.Add(packet.ToString());
        }

        Assert.Equal(["N0CALL>APRS:>One", "N0CALL>APRS:>Two"], packets);
    }

    [Fact]
    public async Task WritesAKissDataFrame()
    {
        await using var port = new FakeSerialPort([]);
        await using var client = new KissStreamClient(port, tncPort: 2);

        await client.SendAsync(AprsPacket.Parse("N0CALL>APRS:>Hi"), TestContext.Current.CancellationToken);

        var frame = Assert.Single(new KissDecoder().Decode(port.Written.ToArray()));
        Assert.Equal(2, frame.Port);
        Assert.True(frame.TryGetAx25(out var ax25));
        Assert.Equal("N0CALL>APRS:>Hi", ax25!.ToTnc2());
    }

    [Fact]
    public async Task DisposesTheStreamOnlyWhenItOwnsIt()
    {
        var owned = new FakeSerialPort([]);
        var borrowed = new FakeSerialPort([]);

        await new KissStreamClient(owned).DisposeAsync();
        await new KissStreamClient(borrowed, ownsStream: false).DisposeAsync();

        Assert.True(owned.Disposed);
        Assert.False(borrowed.Disposed);
        await borrowed.DisposeAsync();
    }

    [Fact]
    public void RefusesAStreamItCannotBothReadAndWrite()
    {
        using var readOnly = new MemoryStream([], writable: false);

        Assert.Throws<ArgumentException>(() => new KissStreamClient(readOnly));
    }

    [Fact]
    public async Task EachFrameGoesToThePortInOneWriteEvenWhenSentTogether()
    {
        // A TNC with little buffering, such as the TNC-X, needs a frame whole rather than in pieces or interleaved.
        var port = new FakeSerialPort([]);
        await using var client = new KissStreamClient(port, transmitLimit: KissTransmitLimit.Off);
        var frames = Enumerable
            .Range(0, 20)
            .Select(i =>
                Ax25Frame.FromAprsPacket(AprsPacket.Parse($"N0CALL-{i % 16}>APZNET:>frame {i} " + new string('x', 100)))
            )
            .ToList();

        await Task.WhenAll(
            frames.Select(f => client.SendFrameAsync(f, TestContext.Current.CancellationToken).AsTask())
        );

        Assert.Equal(frames.Count, port.Writes.Count);
        Assert.All(
            port.Writes,
            write =>
            {
                var decoded = Assert.Single(new KissDecoder().Decode(write));
                Assert.True(decoded.TryGetAx25(out _));
            }
        );
    }
}
