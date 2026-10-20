using System.Text;
using System.Threading.Channels;

namespace AprsNet.Gps.Tests;

/// <summary>gpsd's end of a connection: the test sends report lines and reads back what the client wrote.</summary>
internal sealed class FakeGpsd : Stream
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private byte[] _pending = [];
    private readonly StringBuilder _written = new();

    /// <summary>What the client wrote: its <c>?WATCH</c> command.</summary>
    public string Written
    {
        get
        {
            lock (_written)
            {
                return _written.ToString();
            }
        }
    }

    public void Send(params string[] lines)
    {
        foreach (var line in lines)
        {
            _incoming.Writer.TryWrite(Encoding.UTF8.GetBytes(line + "\r\n"));
        }
    }

    /// <summary>gpsd closes the connection once what was sent has been read.</summary>
    public void End() => _incoming.Writer.TryComplete();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_pending.Length == 0)
        {
            if (!await _incoming.Reader.WaitToReadAsync(cancellationToken))
            {
                return 0;
            }

            _pending = await _incoming.Reader.ReadAsync(cancellationToken);
        }

        var count = Math.Min(buffer.Length, _pending.Length);
        _pending.AsSpan(0, count).CopyTo(buffer.Span);
        _pending = _pending[count..];
        return count;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        lock (_written)
        {
            _written.Append(Encoding.ASCII.GetString(buffer.Span));
        }

        return ValueTask.CompletedTask;
    }

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
