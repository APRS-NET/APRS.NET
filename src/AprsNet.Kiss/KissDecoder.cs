namespace AprsNet.Kiss;

/// <summary>
/// Reads KISS frames from a byte stream (a TCP connection or serial port) that may split them anywhere: feed it each
/// read with <see cref="Decode"/>, and it returns the frames completed by that read. Not thread-safe; use one per
/// stream.
/// </summary>
/// <remarks>
/// <para>
/// Bytes before the first <c>FEND</c>, empty frames (TNCs send <c>FEND FEND</c> to resynchronise), frames with an
/// invalid escape, and frames longer than <see cref="MaxFrameLength"/> are dropped and counted in
/// <see cref="DroppedFrames"/>, so noise on the line never grows the buffer without bound. The bytes lost that way are
/// counted in <see cref="DiscardedBytes"/>.
/// </para>
/// <para>
/// A TNC that isn't in KISS mode sends text lines (<c>SRC&gt;DEST,PATH:info</c>), which a KISS reader would drop
/// without a trace. <see cref="LooksLikeText"/> says when the bytes since the last <c>FEND</c> look like that: at least
/// 128 bytes with two line breaks, nine in ten of them printable. A good frame clears it.
/// </para>
/// </remarks>
public sealed class KissDecoder
{
    private readonly List<byte> _frame = [];
    private bool _inFrame;
    private bool _escaped;
    private bool _discarding;
    private int _run;
    private int _runPrintable;
    private int _runLineBreaks;

    /// <summary>How many bytes since the last <c>FEND</c> must look like text before <see cref="LooksLikeText"/>.</summary>
    internal const int TextRun = 128;

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

    /// <summary>Bytes that were not part of a frame passed on: before the first <c>FEND</c>, or in a dropped frame.</summary>
    public long DiscardedBytes { get; private set; }

    /// <summary>
    /// Whether the bytes arriving look like text lines rather than KISS frames, as from a TNC that isn't in KISS mode.
    /// Set once a run of text is seen since the last <c>FEND</c>; cleared by the next good frame.
    /// </summary>
    public bool LooksLikeText { get; private set; }

    /// <summary>
    /// How many times <see cref="LooksLikeText"/> has been set, so a reader can tell text came even when a frame
    /// cleared it within the same <see cref="Decode"/>.
    /// </summary>
    internal long TextSpotted { get; private set; }

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
                    LooksLikeText = false;
                }
                else if (_inFrame && (_discarding || _escaped))
                {
                    DroppedFrames++;
                    DiscardedBytes += _frame.Count;
                }

                _frame.Clear();
                (_inFrame, _escaped, _discarding) = (true, false, false);
                (_run, _runPrintable, _runLineBreaks) = (0, 0, 0);
                continue;
            }

            Sniff(b);
            if (!_inFrame || _discarding)
            {
                DiscardedBytes++;
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

    /// <summary>Counts <paramref name="b"/> towards the run since the last <c>FEND</c>, to spot text lines.</summary>
    private void Sniff(byte b)
    {
        if (_run == int.MaxValue)
        {
            return;
        }

        _run++;
        if (b is (byte)'\r' or (byte)'\n')
        {
            _runLineBreaks++;
            _runPrintable++;
        }
        else if (b is (byte)'\t' or (>= 0x20 and < 0x7F))
        {
            _runPrintable++;
        }

        if (!LooksLikeText && _run >= TextRun && _runLineBreaks >= 2 && _runPrintable * 10L >= _run * 9L)
        {
            LooksLikeText = true;
            TextSpotted++;
        }
    }

    private void Add(byte b)
    {
        if (_frame.Count == MaxFrameLength)
        {
            _discarding = true;
            DiscardedBytes += _frame.Count + 1;
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
