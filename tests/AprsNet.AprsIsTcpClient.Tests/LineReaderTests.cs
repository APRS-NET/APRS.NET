using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace AprsNet.AprsIsTcpClient.Tests;

public class LineReaderTests
{
    private static LineReader Reader(Stream stream, int max = 64) => new(stream, max, NullLogger.Instance);

    private static async Task<List<string>> ReadAll(LineReader reader)
    {
        var lines = new List<string>();
        while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    [Fact]
    public async Task Reads_lf_and_crlf_lines()
    {
        var reader = Reader(new MemoryStream("one\r\ntwo\nthree\r\n"u8.ToArray()));
        Assert.Equal(["one", "two", "three"], await ReadAll(reader));
    }

    [Fact]
    public async Task Returns_empty_lines()
    {
        var reader = Reader(new MemoryStream("a\r\n\r\nb\r\n"u8.ToArray()));
        Assert.Equal(["a", "", "b"], await ReadAll(reader));
    }

    [Fact]
    public async Task Tolerates_one_byte_reads()
    {
        var reader = Reader(new TrickleStream("N0CALL>APRS:>hi\r\nsecond\r\n"u8.ToArray()));
        Assert.Equal(["N0CALL>APRS:>hi", "second"], await ReadAll(reader));
    }

    [Fact]
    public async Task Drops_an_unterminated_final_line()
    {
        var reader = Reader(new MemoryStream("whole\r\npartial"u8.ToArray()));
        Assert.Equal(["whole"], await ReadAll(reader));
    }

    [Fact]
    public async Task Accepts_a_line_of_exactly_the_limit()
    {
        var line = new string('a', 64);
        var reader = Reader(new TrickleStream(Encoding.ASCII.GetBytes(line + "\r\nnext\r\n")), 64);
        Assert.Equal([line, "next"], await ReadAll(reader));
        Assert.Equal(0, reader.DiscardedLines);
    }

    [Theory]
    [InlineData(65)]
    [InlineData(66)]
    [InlineData(500)]
    public async Task Discards_overlong_lines_and_recovers(int length)
    {
        var data = Encoding.ASCII.GetBytes(new string('x', length) + "\r\nok\r\n");
        foreach (var stream in new Stream[] { new MemoryStream(data), new TrickleStream(data) })
        {
            var reader = Reader(stream, 64);
            Assert.Equal(["ok"], await ReadAll(reader));
            Assert.Equal(1, reader.DiscardedLines);
        }
    }

    [Fact]
    public async Task Discarding_does_not_buffer_an_endless_line()
    {
        var reader = Reader(new EndlessStream(), 64);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadLineAsync(cts.Token));
    }

    [Fact]
    public async Task Preserves_every_byte_as_latin1()
    {
        byte[] raw = [0x60, 0x1C, 0x7F, 0x80, 0xFF, 0x0D, 0x0A];
        var line = await Reader(new MemoryStream(raw)).ReadLineAsync(CancellationToken.None);
        Assert.Equal("`\u001c\u007f\u0080ÿ", line);
    }

    /// <summary>Returns one byte per read, like a worst-case network.</summary>
    private sealed class TrickleStream(byte[] data) : Stream
    {
        private int _position;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= data.Length || buffer.Length == 0)
            {
                return ValueTask.FromResult(0);
            }

            buffer.Span[0] = data[_position++];
            return ValueTask.FromResult(1);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
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

    /// <summary>Never sends a newline.</summary>
    private sealed class EndlessStream : Stream
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Span.Fill((byte)'x');
            await Task.Yield();
            return buffer.Length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
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
