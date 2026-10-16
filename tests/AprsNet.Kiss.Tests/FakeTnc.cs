using System.Threading.Channels;

namespace AprsNet.Kiss.Tests;

/// <summary>A TNC's end of a KISS link: the test feeds what it "hears" and reads back what the client sent.</summary>
internal sealed class FakeTnc : Stream
{
    private readonly Channel<object> _incoming = Channel.CreateUnbounded<object>();

    public MemoryStream Written { get; } = new();

    public void Send(byte[] bytes) => _incoming.Writer.TryWrite(bytes);

    public void End() => _incoming.Writer.TryComplete();

    public void Fail(Exception error) => _incoming.Writer.TryWrite(error);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
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
