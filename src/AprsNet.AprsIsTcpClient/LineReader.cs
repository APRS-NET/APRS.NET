using System.Text;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsTcpClient;

/// <summary>
///     Splits a byte stream into lines with a hard length limit. Tolerates split reads, LF or CRLF terminators,
///     and discards over-long lines without buffering them.
/// </summary>
internal sealed class LineReader(Stream stream, int maxLineLength, ILogger logger)
{
    private readonly byte[] _buffer = new byte[maxLineLength + 2]; // a full line plus CRLF
    private bool _discarding;
    private int _end;
    private int _start;

    /// <summary>Lines dropped for exceeding the length limit.</summary>
    public long DiscardedLines { get; private set; }

    /// <summary>Returns the next line (possibly empty), or null at end of stream. An unterminated final line is dropped.</summary>
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

                if (line.Length > 0 && line[^1] == (byte)'\r')
                    line = line[..^1];

                if (line.Length > maxLineLength)
                {
                    Discarded(line.Length);
                    continue;
                }

                return Encoding.Latin1.GetString(line);
            }

            if (_discarding)
            {
                _start = _end = 0;
            }
            else if (pending.Length > maxLineLength + 1)
            {
                Discarded(pending.Length);
                _discarding = true;
                _start = _end = 0;
            }
            else if (_start > 0)
            {
                pending.CopyTo(_buffer);
                _end = pending.Length;
                _start = 0;
            }

            var read = await stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;

            _end += read;
        }
    }

    private void Discarded(int length)
    {
        DiscardedLines++;
        logger.OverlongLineDiscarded(maxLineLength, length);
    }
}
