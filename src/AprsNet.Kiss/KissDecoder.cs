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
/// 128 bytes with two line breaks, nine in ten of them printable. A good frame clears it. Bytes that look like text are
/// not a frame, even after a <c>FEND</c>: from then until the next <c>FEND</c> they are discarded as outside any frame.
/// So are bytes after a <c>FEND</c> whose first isn't a KISS command byte (low four bits 0 to 6, or <c>FF</c>), such as
/// the answers of a TNC's command line, which start with a line break or a letter.
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
    private KissDropReason _dropReason;

    // Kept only while Trace is set: the raw bytes of the frame so far, and bytes outside any frame not yet reported.
    private readonly List<byte> _raw = [];
    private readonly List<byte> _unframed = [];

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

    /// <summary>Told about every frame and every byte outside one, for a traffic view; null, the default, for none.</summary>
    internal IKissDecoderTrace? Trace { get; set; }

    /// <summary>Raw bytes held for <see cref="Trace"/>; none while there is no trace.</summary>
    internal int RawBuffered => _raw.Count + _unframed.Count;

    /// <summary>Takes the next bytes from the stream; returns the frames they complete, in order.</summary>
    public IReadOnlyList<KissFrame> Decode(ReadOnlySpan<byte> bytes)
    {
        List<KissFrame>? frames = null;
        var trace = Trace;
        if (trace is null)
        {
            _raw.Clear();
            _unframed.Clear();
        }

        foreach (var b in bytes)
        {
            if (b == KissFrame.FrameEnd)
            {
                FlushUnframed(trace);
                if (_inFrame && !_discarding && !_escaped && _frame.Count > 0)
                {
                    var frame = Complete();
                    (frames ??= []).Add(frame);
                    LooksLikeText = false;
                    trace?.Frame(Raw(), frame);
                }
                else if (_inFrame && (_discarding || _escaped))
                {
                    DroppedFrames++;
                    DiscardedBytes += _frame.Count;
                    trace?.Dropped(Raw(), _escaped ? KissDropReason.InvalidEscape : _dropReason);
                }

                _frame.Clear();
                _raw.Clear();
                if (trace is not null)
                {
                    _raw.Add(b);
                }

                (_inFrame, _escaped, _discarding) = (true, false, false);
                (_run, _runPrintable, _runLineBreaks) = (0, 0, 0);
                continue;
            }

            Sniff(b, trace);
            if (_inFrame && trace is not null && _raw.Count <= MaxFrameLength * 2 + 2)
            {
                _raw.Add(b); // the frame as it crossed the link, a dropped one too
            }

            if (_inFrame && !_discarding && _frame.Count == 0 && !_escaped && !IsCommandByte(b))
            {
                // A frame starts with a command byte (low four bits 0 to 6, or FF). Anything else, such as the CR or
                // letter a TNC's command line starts its answers with, is outside any frame until the next FEND.
                if (trace is not null && _raw.Count > 0)
                {
                    _raw.RemoveAt(_raw.Count - 1); // b went in as the frame's first byte
                }

                _raw.Clear();
                _inFrame = false;
            }

            if (!_inFrame || _discarding)
            {
                DiscardedBytes++;
                if (!_inFrame && trace is not null)
                {
                    _unframed.Add(b);
                }

                continue; // noise before the first FEND, text, or the rest of a dropped frame
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
                        _dropReason = KissDropReason.InvalidEscape;
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

        FlushUnframed(trace);
        return frames ?? (IReadOnlyList<KissFrame>)[];
    }

    /// <summary>The frame's raw bytes so far, with the FEND that closed it.</summary>
    private byte[] Raw()
    {
        _raw.Add(KissFrame.FrameEnd);
        return [.. _raw];
    }

    private void FlushUnframed(IKissDecoderTrace? trace)
    {
        if (trace is not null && _unframed.Count > 0)
        {
            trace.Unframed([.. _unframed]);
            _unframed.Clear();
        }
    }

    private static bool IsCommandByte(byte b) => b == 0xFF || (b & 0x0F) <= (int)KissCommand.SetHardware;

    /// <summary>Counts <paramref name="b"/> towards the run since the last <c>FEND</c>, to spot text lines.</summary>
    private void Sniff(byte b, IKissDecoderTrace? trace)
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
            if (_inFrame)
            {
                // Text after a FEND, as from a TNC that lost KISS mode: it isn't a frame, so it is outside one.
                DiscardedBytes += _discarding ? 0 : _frame.Count;
                if (trace is not null && _raw.Count > 1)
                {
                    _unframed.AddRange(_raw.Skip(1)); // without the FEND that opened it
                }

                _frame.Clear();
                _raw.Clear();
                (_inFrame, _escaped, _discarding) = (false, false, false);
            }
        }
    }

    private void Add(byte b)
    {
        if (_frame.Count == MaxFrameLength)
        {
            _discarding = true;
            _dropReason = KissDropReason.TooLong;
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

/// <summary>What a <see cref="KissDecoder"/> tells a traffic view: every frame, and every byte outside one.</summary>
internal interface IKissDecoderTrace
{
    /// <summary>Bytes outside any frame, in the order they came; called at a FEND and at the end of each read.</summary>
    void Unframed(byte[] bytes);

    /// <summary>A frame decoded, with its raw bytes from the opening to the closing FEND.</summary>
    void Frame(byte[] raw, KissFrame frame);

    /// <summary>A frame dropped, with its raw bytes (at most about twice the longest frame) and why.</summary>
    void Dropped(byte[] raw, KissDropReason reason);
}
