using System.Text;

namespace AprsNet.Gateway;

/// <summary>
/// Reads LF or CRLF terminated Latin-1 lines from a client, dropping any line longer than the limit instead of
/// buffering it, so a client cannot make the server hold an unbounded line.
/// </summary>
internal sealed class BoundedLineReader(Stream stream, int maxLineLength)
{
    private readonly byte[] _buffer = new byte[maxLineLength + 2];
    private int _start;
    private int _end;
    private bool _discarding;

    /// <summary>The next line without its terminator, or <see langword="null"/> when the client has closed.</summary>
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pending = _buffer.AsSpan(_start, _end - _start);
            var newline = pending.IndexOf((byte)'\n');
            if (newline >= 0)
            {
                var line = pending[..newline];
                _start += newline + 1;
                if (_discarding)
                {
                    _discarding = false;
                    continue;
                }

                return Encoding.Latin1.GetString(line.EndsWith("\r"u8) ? line[..^1] : line);
            }

            if (_start > 0)
            {
                pending.CopyTo(_buffer);
                _end -= _start;
                _start = 0;
            }

            if (_end == _buffer.Length)
            {
                // Too long: drop what is buffered and the rest of the line.
                _discarding = true;
                _end = 0;
            }

            var read = await stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            _end += read;
        }
    }
}
