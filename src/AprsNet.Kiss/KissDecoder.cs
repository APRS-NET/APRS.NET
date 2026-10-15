namespace AprsNet.Kiss;

/// <summary>
/// Reads KISS frames from a byte stream (a TCP connection or serial port) that may split them anywhere: feed it each
/// read with <see cref="Decode"/>, and it returns the frames completed by that read. Not thread-safe; use one per
/// stream.
/// </summary>
/// <remarks>
/// Bytes before the first <c>FEND</c>, empty frames (TNCs send <c>FEND FEND</c> to resynchronise), frames with an
/// invalid escape, and frames longer than <see cref="MaxFrameLength"/> are dropped and counted in
/// <see cref="DroppedFrames"/>, so noise on the line never grows the buffer without bound.
/// </remarks>
public sealed class KissDecoder
{
    private readonly List<byte> _frame = [];
    private bool _inFrame;
    private bool _escaped;
    private bool _discarding;

    /// <summary>Creates a decoder.</summary>
    /// <param name="maxFrameLength">
    /// The longest frame to accept, after unescaping and including the command byte. AX.25 frames for APRS are at most
    /// a few hundred bytes; the default leaves ample room.
    /// </param>
    public KissDecoder(int maxFrameLength = 2048)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 2);
        MaxFrameLength = maxFrameLength;
    }

    /// <summary>The longest frame accepted, including the command byte.</summary>
    public int MaxFrameLength { get; }

    /// <summary>Frames dropped for an invalid escape or for being too long.</summary>
    public long DroppedFrames { get; private set; }

    /// <summary>Takes the next bytes from the stream; returns the frames they complete, in order.</summary>
    public IReadOnlyList<KissFrame> Decode(ReadOnlySpan<byte> bytes)
    {
        List<KissFrame>? frames = null;
        foreach (var b in bytes)
        {
            if (b == KissFrame.FrameEnd)
            {
                if (_inFrame && !_discarding && !_escaped && _frame.Count > 0)
                {
                    (frames ??= []).Add(Complete());
                }
                else if (_inFrame && (_discarding || _escaped))
                {
                    DroppedFrames++;
                }

                _frame.Clear();
                (_inFrame, _escaped, _discarding) = (true, false, false);
                continue;
            }

            if (!_inFrame || _discarding)
            {
                continue; // noise before the first FEND, or the rest of a dropped frame
            }

            if (_escaped)
            {
                _escaped = false;
                switch (b)
                {
                    case KissFrame.TransposedFrameEnd:
                        Add(KissFrame.FrameEnd);
                        break;
                    case KissFrame.TransposedFrameEscape:
                        Add(KissFrame.FrameEscape);
                        break;
                    default:
                        _discarding = true; // FESC followed by anything else: the frame is corrupt
                        break;
                }
            }
            else if (b == KissFrame.FrameEscape)
            {
                _escaped = true;
            }
            else
            {
                Add(b);
            }
        }

        return frames ?? (IReadOnlyList<KissFrame>)[];
    }

    private void Add(byte b)
    {
        if (_frame.Count == MaxFrameLength)
        {
            _discarding = true;
            _frame.Clear();
            return;
        }

        _frame.Add(b);
    }

    private KissFrame Complete()
    {
        var command = _frame[0];
        var payload = _frame.Count > 1 ? _frame.GetRange(1, _frame.Count - 1).ToArray() : [];
        return command == 0xFF
            ? new KissFrame(0, KissCommand.Return, payload)
            : new KissFrame(command >> 4, (KissCommand)(command & 0x0F), payload);
    }
}
