using System.Text;
using System.Threading.Channels;

namespace AprsNet.AprsIsCli.Tests;

/// <summary>
/// A TNC's command line on a serial port, answering as a TH-D72A did when recorded: echoes each command and prompts
/// with cmd:, answers the KISS exit frame at the prompt with ?EH, KISS ON with "KISS     was OFF", and RESTART with
/// silence. A silent one never answers, like a radio in APRS mode.
/// </summary>
internal sealed class FakeCommandLine(bool silent = false) : Stream
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly MemoryStream _written = new();
    private bool _badCommand;

    public byte[] Written
    {
        get
        {
            lock (_written)
            {
                return _written.ToArray();
            }
        }
    }

    public string WrittenText => Encoding.Latin1.GetString(Written);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!await _incoming.Reader.WaitToReadAsync(cancellationToken))
        {
            return 0;
        }

        var bytes = await _incoming.Reader.ReadAsync(cancellationToken);
        bytes.CopyTo(buffer);
        return bytes.Length;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        lock (_written)
        {
            _written.Write(buffer.Span);
        }

        if (!silent)
        {
            Answer(Encoding.Latin1.GetString(buffer.Span));
        }

        return ValueTask.CompletedTask;
    }

    private void Answer(string text)
    {
        var answer = text switch
        {
            "\xC0\xFF\xC0" => Mark(text),
            "\r" => _badCommand ? "\r\n?EH\r\ncmd:" : "\r\ncmd:",
            "KISS ON\r" => "KISS ON\r\nKISS     was OFF\r\ncmd:",
            "RESTART\r" => "R",
            _ when text.EndsWith('\r') => text.TrimEnd('\r') + "\r\ncmd:",
            _ => null,
        };
        if (text == "\r")
        {
            _badCommand = false;
        }

        if (answer is not null)
        {
            _incoming.Writer.TryWrite(Encoding.Latin1.GetBytes(answer));
        }
    }

    private string Mark(string text)
    {
        _badCommand = true;
        return text;
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

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    protected override void Dispose(bool disposing)
    {
        _incoming.Writer.TryComplete();
        base.Dispose(disposing);
    }
}
