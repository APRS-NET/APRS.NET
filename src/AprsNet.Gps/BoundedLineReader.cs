using System.Text;

namespace AprsNet.Gps;

/// <summary>
/// Reads LF or CRLF terminated lines from a GPS source, dropping any line longer than the limit instead of buffering
/// it, so a source that never ends a line cannot make the reader hold an unbounded one.
/// </summary>
internal sealed class BoundedLineReader(Stream stream, int maxLineLength, Encoding encoding)
{
    private readonly byte[] _buffer = new byte[maxLineLength + 2];
    private int _start;
    private int _end;
    private bool _discarding;
    private bool _ended;

    /// <summary>
    /// The next line without its terminator, or <see langword="null"/> when the stream has ended. A last line without
    /// a terminator is still returned, unless it was too long.
    /// </summary>
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

                return encoding.GetString(line.EndsWith("\r"u8) ? line[..^1] : line);
            }

            if (_ended)
            {
                var last = _discarding || pending.IsEmpty ? null : encoding.GetString(pending);
                _start = _end;
                _discarding = false;
                return last;
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
                _ended = true;
                continue;
            }

            _end += read;
        }
    }
}
