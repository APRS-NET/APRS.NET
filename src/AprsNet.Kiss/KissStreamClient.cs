using System.Runtime.CompilerServices;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss;

/// <summary>
/// Speaks KISS over any byte stream an app has already opened: a serial port's <c>BaseStream</c> for a hardware TNC,
/// a Bluetooth link, or a network stream. It reads the AX.25 frames the TNC hears and sends APRS packets for it to
/// transmit. <see cref="KissTcpClient"/> uses it for TCP.
/// </summary>
public sealed partial class KissStreamClient : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly int _tncPort;
    private readonly KissDecoder _decoder = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, KissTransmitLimit> _portLimits = new();
    private Tracer? _tracer;
    private int _disposed;

    /// <summary>Wraps an open stream.</summary>
    /// <param name="stream">A readable and writable stream connected to the TNC.</param>
    /// <param name="tncPort">
    /// The radio port on a multi-port TNC, 0 to 15: frames are sent there, and only frames heard there are read.
    /// </param>
    /// <param name="ownsStream">Whether disposing this client disposes the stream.</param>
    /// <param name="transmitLimit">
    /// How fast frames may be sent; a new <see cref="KissTransmitLimit"/> with the default settings when not given.
    /// </param>
    public KissStreamClient(
        Stream stream,
        int tncPort = 0,
        bool ownsStream = true,
        KissTransmitLimit? transmitLimit = null
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException("The stream must be readable and writable.", nameof(stream));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(tncPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tncPort, 15);
        _stream = stream;
        _tncPort = tncPort;
        _ownsStream = ownsStream;
        TransmitLimit = transmitLimit ?? KissTransmitLimit.Default;
    }

    /// <summary>
    /// How fast frames may be sent to the client's radio port; a send waits for its turn. Each other port gets a limit
    /// of its own with the same settings.
    /// </summary>
    public KissTransmitLimit TransmitLimit { get; }

    /// <summary>
    /// Whether frames heard on every radio port of a multi-port TNC are read, not only the client's own:
    /// <see cref="ReadPortFramesAsync"/> says which port each came from. <see langword="false"/> by default.
    /// </summary>
    public bool AllPorts { get; init; }

    /// <summary>Called after each read that brought bytes, before they are decoded.</summary>
    internal Action? DataRead { get; init; }

    /// <summary>
    /// For a client inside another (a TCP client, a lasting connection) that forwards <see cref="Traffic"/>: whether
    /// anyone listens to the outer one, so the forwarding subscription alone doesn't turn tracing on.
    /// </summary>
    internal Func<bool>? WantsTraffic { get; set; }

    private bool Tracing => Traffic is not null && (WantsTraffic?.Invoke() ?? true);

    /// <summary>
    /// While a script runs on the open connection (<see cref="KissConnection.RunScriptAsync"/>): a copy of every read
    /// goes here as well as to the decoder, so the script's expect steps see the TNC's answers.
    /// </summary>
    internal System.Threading.Channels.ChannelWriter<byte[]>? ScriptFeed { get; set; }

    /// <summary>
    /// Whether the TNC prints each packet it hears as a TNC-2 text line instead of speaking KISS, as a radio in its APRS
    /// mode does (the TH-D72's APRS12): the lines are read as packets, receive only. See <c>KissSerialOptions.TextMode</c>.
    /// </summary>
    internal bool TextMode { get; init; }

    private long _droppedLines;

    /// <summary>The clock for <see cref="KissTrafficEventArgs.At"/>.</summary>
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>The <c>tnc</c> tag on this client's metrics: the TNC's <c>host:port</c> when a TCP client made it.</summary>
    internal string MetricsTnc { get; init; } = KissMetrics.StreamTnc;

    /// <summary>
    /// KISS frames dropped for an invalid escape or for being too long: a sign of a noisy serial link. In text mode,
    /// packet lines dropped as damaged or not AX.25.
    /// </summary>
    public long DroppedFrames => _decoder.DroppedFrames + Interlocked.Read(ref _droppedLines);

    /// <summary>Bytes read that were not part of a frame passed on; see <see cref="KissDecoder.DiscardedBytes"/>.</summary>
    public long DiscardedBytes => _decoder.DiscardedBytes;

    /// <summary>
    /// Fires when the TNC starts sending something other than KISS frames, and again when it stops; on the reading
    /// thread. See <see cref="KissWarning"/>.
    /// </summary>
    public event EventHandler<KissWarningEventArgs>? Warning;

    /// <summary>
    /// In text mode, each GPS sentence the TNC prints between its packet lines, as a radio with GPS PC output on does in
    /// its APRS mode: whole, with a checksum that matches. Raised on the reading thread; an exception thrown by a
    /// handler is ignored. A line starting with <c>$</c> that is cut off or has a wrong checksum isn't raised; it is
    /// reported as <see cref="KissTrafficKind.DamagedNmea"/>.
    /// </summary>
    internal event EventHandler<NmeaSentenceEventArgs>? NmeaSentenceReceived;

    /// <summary>
    /// Everything that crosses the link, for a troubleshooting console: each frame received (APRS or not, for this
    /// port or another, damaged), bytes outside any frame a line at a time, and each frame sent. Received entries
    /// are raised on the reading thread, sent ones on the sending thread. Nothing extra is done while no one is
    /// subscribed; an exception thrown by a handler is ignored. See <see cref="KissTrafficKind"/>.
    /// </summary>
    public event EventHandler<KissTrafficEventArgs>? Traffic;

    /// <summary>
    /// Yields every AX.25 frame the TNC passes on from its radio port (every port, with <see cref="AllPorts"/>), APRS or
    /// not, until the stream ends. Frames that are not valid AX.25 are skipped. Intended for a single consumer.
    /// </summary>
    public async IAsyncEnumerable<Ax25Frame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var heard in ReadPortFramesAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return heard.Frame;
        }
    }

    /// <summary>
    /// As <see cref="ReadFramesAsync"/>, with the radio port each frame was heard on: for a multi-port TNC read with
    /// <see cref="AllPorts"/>. Use one of the read methods, not several.
    /// </summary>
    public async IAsyncEnumerable<KissPortFrame> ReadPortFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (TextMode)
        {
            await foreach (var line in ReadTextFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return new KissPortFrame(_tncPort, line);
            }

            yield break;
        }

        var buffer = new byte[4096];
        while (true)
        {
            var read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                yield break;
            }

            DataRead?.Invoke();
            ScriptFeed?.TryWrite(buffer.AsSpan(0, read).ToArray());

            var (dropped, discarded, text, spotted) = (
                _decoder.DroppedFrames,
                _decoder.DiscardedBytes,
                _decoder.LooksLikeText,
                _decoder.TextSpotted
            );
            _decoder.Trace = Tracing ? _tracer ??= new Tracer(this) : null;
            var decoded = _decoder.Decode(buffer.AsSpan(0, read));
            if (_decoder.DroppedFrames > dropped)
            {
                KissMetrics.Dropped(MetricsTnc, _decoder.DroppedFrames - dropped);
            }

            if (_decoder.DiscardedBytes > discarded)
            {
                KissMetrics.Discarded(MetricsTnc, _decoder.DiscardedBytes - discarded);
            }

            // Text may start and stop within one read, when a frame follows it closely; report both.
            var started = _decoder.TextSpotted > spotted;
            if (started && !text)
            {
                Warning?.Invoke(this, new KissWarningEventArgs(KissWarning.TextNotKiss, true));
            }

            if (!_decoder.LooksLikeText && (text || started))
            {
                Warning?.Invoke(this, new KissWarningEventArgs(KissWarning.TextNotKiss, false));
            }

            foreach (var kiss in decoded)
            {
                if ((AllPorts || kiss.Port == _tncPort) && kiss.TryGetAx25(out var frame))
                {
                    KissMetrics.Received(MetricsTnc, kiss.Port);
                    yield return new KissPortFrame(kiss.Port, frame!);
                }
            }
        }
    }

    /// <summary>
    /// Yields the APRS packets among the frames (<see cref="Ax25Frame.IsAprs"/>), each parsed from its TNC2 line, until
    /// the stream ends. Use this or <see cref="ReadFramesAsync"/>, not both.
    /// </summary>
    public async IAsyncEnumerable<AprsPacket> ReadPacketsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var frame in ReadFramesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (frame.IsAprs)
            {
                yield return frame.ToAprsPacket();
            }
        }
    }

    /// <summary>
    /// Sends <paramref name="packet"/> for the TNC to transmit on its radio port, once <see cref="TransmitLimit"/>
    /// allows. The TNC handles channel access; it may hold the frame until the channel is clear.
    /// </summary>
    /// <exception cref="ArgumentException">The packet cannot be sent as AX.25; see <see cref="Ax25Frame.FromAprsPacket"/>.</exception>
    public ValueTask SendAsync(AprsPacket packet, CancellationToken cancellationToken = default) =>
        SendFrameAsync(Ax25Frame.FromAprsPacket(packet), cancellationToken);

    /// <summary>
    /// Sends an AX.25 frame for the TNC to transmit on its radio port, once <see cref="TransmitLimit"/> allows.
    /// </summary>
    public ValueTask SendFrameAsync(Ax25Frame frame, CancellationToken cancellationToken = default) =>
        SendFrameToPortAsync(_tncPort, frame, cancellationToken);

    /// <summary>
    /// Sends <paramref name="packet"/> for the TNC to transmit on radio port <paramref name="port"/> of a multi-port TNC,
    /// once that port's transmit limit allows.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> isn't 0 to 15.</exception>
    /// <exception cref="ArgumentException">The packet cannot be sent as AX.25; see <see cref="Ax25Frame.FromAprsPacket"/>.</exception>
    public ValueTask SendToPortAsync(int port, AprsPacket packet, CancellationToken cancellationToken = default) =>
        SendFrameToPortAsync(port, Ax25Frame.FromAprsPacket(packet), cancellationToken);

    /// <summary>
    /// Sends an AX.25 frame for the TNC to transmit on radio port <paramref name="port"/> of a multi-port TNC, once that
    /// port's transmit limit allows: <see cref="TransmitLimit"/> for the client's own port, and one with the same
    /// settings for each other port.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> isn't 0 to 15.</exception>
    public async ValueTask SendFrameToPortAsync(
        int port,
        Ax25Frame frame,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 15);
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (TextMode)
        {
            throw new InvalidOperationException(
                "This TNC prints packets as text (a radio in APRS mode) and takes nothing to transmit: switch it to "
                    + "KISS mode to send."
            );
        }

        var bytes = KissFrame.ForAx25(frame, port).Encode();
        await LimitFor(port).WaitAsync(cancellationToken).ConfigureAwait(false);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            KissMetrics.Sent(MetricsTnc, port);
            if (Tracing)
            {
                Raise(
                    new KissTrafficEventArgs(TimeProvider.GetUtcNow(), KissTrafficKind.Sent, bytes)
                    {
                        Port = port,
                        Command = KissCommand.Data,
                        Frame = frame,
                        Packet = frame.IsAprs ? frame.ToAprsPacket() : null,
                    }
                );
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>The transmit limit for <paramref name="port"/>: one per port, each like <see cref="TransmitLimit"/>.</summary>
    internal KissTransmitLimit LimitFor(int port) =>
        port == _tncPort ? TransmitLimit : _portLimits.GetOrAdd(port, _ => TransmitLimit.CreateLike());

    /// <summary>
    /// Writes <paramref name="bytes"/> as they are, outside any frame, under the same lock as frames so they never
    /// land inside one. Not limited by <see cref="TransmitLimit"/>. Reported as <paramref name="kind"/>, or not at all.
    /// </summary>
    internal async ValueTask SendRawAsync(
        ReadOnlyMemory<byte> bytes,
        KissTrafficKind? kind,
        CancellationToken cancellationToken
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        if (kind is { } reported && Tracing)
        {
            Raise(new KissTrafficEventArgs(TimeProvider.GetUtcNow(), reported, bytes.ToArray()));
        }
    }

    /// <summary>
    /// Text mode: reads TNC-2 lines and yields each packet as the AX.25 frame it came off the air as. GPS sentences
    /// (<c>$GP…</c>) go to <see cref="NmeaSentenceReceived"/>, and other lines without a
    /// <c>SOURCE&gt;DESTINATION:</c> header are skipped; a packet line with a sentence run into it, or one AX.25 can't
    /// carry, is dropped and counted.
    /// </summary>
    private async IAsyncEnumerable<Ax25Frame> ReadTextFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        const int maxLine = 1024;
        var buffer = new byte[4096];
        var line = new List<byte>(256);
        var overlong = false;
        while (true)
        {
            var read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                yield break;
            }

            DataRead?.Invoke();
            ScriptFeed?.TryWrite(buffer.AsSpan(0, read).ToArray());
            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                if (b is not ((byte)'\r' or (byte)'\n'))
                {
                    if (line.Count < maxLine)
                    {
                        line.Add(b);
                    }
                    else
                    {
                        overlong = true;
                    }

                    continue;
                }

                if (line.Count == 0)
                {
                    continue;
                }

                var frame = TextLine([.. line], overlong);
                line.Clear();
                overlong = false;
                if (frame is not null)
                {
                    KissMetrics.Received(MetricsTnc, _tncPort);
                    yield return frame;
                }
            }
        }
    }

    /// <summary>One text line: the frame of the packet it holds, or null for a line skipped or dropped.</summary>
    private Ax25Frame? TextLine(byte[] bytes, bool overlong)
    {
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        if (text[0] == '$')
        {
            GpsLine(bytes, text); // a sentence, or one damaged where another line ran into it
            return null;
        }

        if (overlong || TextLineDamage().IsMatch(text))
        {
            return DropLine(bytes, KissDropReason.DamagedLine);
        }

        var packet = AprsPacket.Parse(text);
        if (!packet.HasHeader)
        {
            if (Tracing)
            {
                Raise(
                    new KissTrafficEventArgs(TimeProvider.GetUtcNow(), KissTrafficKind.Unframed, bytes)
                    {
                        LooksLikeText = true,
                        EndsLine = true,
                    }
                );
            }

            return null; // a prompt, a blank: not a packet
        }

        Ax25Frame frame;
        try
        {
            frame = Ax25Frame.FromAprsPacket(packet);
        }
        catch (ArgumentException)
        {
            return DropLine(bytes, KissDropReason.NotAx25);
        }

        if (Tracing)
        {
            Raise(
                new KissTrafficEventArgs(TimeProvider.GetUtcNow(), KissTrafficKind.Frame, bytes)
                {
                    Frame = frame,
                    Packet = packet,
                }
            );
        }

        return frame;
    }

    /// <summary>
    /// A text line starting with <c>$</c>: a GPS sentence when it is whole and its checksum matches, else damaged, as
    /// when a packet line or another sentence ran into it.
    /// </summary>
    private void GpsLine(byte[] bytes, string text)
    {
        var at = TimeProvider.GetUtcNow();
        if (!NmeaSentence.TryParse(text, out var sentence) || sentence.Checksum is null || !sentence.IsChecksumValid)
        {
            KissMetrics.NmeaDamaged(MetricsTnc);
            if (Tracing)
            {
                Raise(
                    new KissTrafficEventArgs(at, KissTrafficKind.DamagedNmea, bytes)
                    {
                        LooksLikeText = true,
                        EndsLine = true,
                    }
                );
            }

            return;
        }

        KissMetrics.NmeaReceived(MetricsTnc);
        if (Tracing)
        {
            Raise(
                new KissTrafficEventArgs(at, KissTrafficKind.Nmea, bytes)
                {
                    LooksLikeText = true,
                    EndsLine = true,
                    Sentence = sentence,
                }
            );
        }

        try
        {
            NmeaSentenceReceived?.Invoke(this, new NmeaSentenceEventArgs(sentence, at));
        }
        catch (Exception)
        {
            // An app's bug must not stop the packets.
        }
    }

    private Ax25Frame? DropLine(byte[] bytes, KissDropReason reason)
    {
        Interlocked.Increment(ref _droppedLines);
        KissMetrics.Dropped(MetricsTnc, 1);
        if (Tracing)
        {
            Raise(
                new KissTrafficEventArgs(TimeProvider.GetUtcNow(), KissTrafficKind.DroppedFrame, bytes)
                {
                    DropReason = reason,
                }
            );
        }

        return null;
    }

    /// <summary>A GPS sentence that ran into a line, anywhere but its start: <c>$GPGGA,</c>, <c>$GNRMC,</c> and so on.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"(?<!^)\$G[A-Z][A-Z]{3},")]
    private static partial System.Text.RegularExpressions.Regex TextLineDamage();

    /// <summary>Raises <see cref="Traffic"/>, ignoring an exception a handler throws so reading carries on.</summary>
    private void Raise(KissTrafficEventArgs entry)
    {
        try
        {
            Traffic?.Invoke(this, entry);
        }
        catch (Exception)
        {
            // A console's bug must not stop the packets.
        }
    }

    /// <summary>Turns what the decoder reports into traffic entries, splitting bytes outside frames into lines.</summary>
    private sealed class Tracer(KissStreamClient client) : IKissDecoderTrace
    {
        public void Unframed(byte[] bytes)
        {
            var start = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                // A line ends at LF, at CR not followed by LF in this read, and at CR LF together.
                var b = bytes[i];
                if (b == (byte)'\n' || (b == (byte)'\r' && (i + 1 == bytes.Length || bytes[i + 1] != (byte)'\n')))
                {
                    Line(bytes.AsMemory(start, i + 1 - start), endsLine: true);
                    start = i + 1;
                }
            }

            if (start < bytes.Length)
            {
                Line(bytes.AsMemory(start), endsLine: false);
            }
        }

        public void Frame(byte[] raw, KissFrame frame)
        {
            var at = client.TimeProvider.GetUtcNow();
            if (frame.Port != client._tncPort && !client.AllPorts)
            {
                client.Raise(Entry(at, KissTrafficKind.OtherPort, raw, frame));
                return;
            }

            if (frame.Command != KissCommand.Data)
            {
                client.Raise(Entry(at, KissTrafficKind.Command, raw, frame));
                return;
            }

            var ax25 = frame.TryGetAx25(out var parsed) ? parsed : null;
            var aprs = ax25 is { IsAprs: true };
            client.Raise(
                new KissTrafficEventArgs(at, aprs ? KissTrafficKind.Frame : KissTrafficKind.NotAprs, raw)
                {
                    Port = frame.Port,
                    Command = frame.Command,
                    Frame = ax25,
                    Packet = aprs ? ax25!.ToAprsPacket() : null,
                }
            );
        }

        public void Dropped(byte[] raw, KissDropReason reason) =>
            client.Raise(
                new KissTrafficEventArgs(client.TimeProvider.GetUtcNow(), KissTrafficKind.DroppedFrame, raw)
                {
                    DropReason = reason,
                }
            );

        private void Line(ReadOnlyMemory<byte> line, bool endsLine)
        {
            var printable = 0;
            foreach (var b in line.Span)
            {
                printable += b is (byte)'\r' or (byte)'\n' or (byte)'\t' or (>= 0x20 and < 0x7F) ? 1 : 0;
            }

            client.Raise(
                new KissTrafficEventArgs(client.TimeProvider.GetUtcNow(), KissTrafficKind.Unframed, line.ToArray())
                {
                    LooksLikeText = printable * 10 >= line.Length * 9,
                    EndsLine = endsLine,
                }
            );
        }

        private static KissTrafficEventArgs Entry(
            DateTimeOffset at,
            KissTrafficKind kind,
            byte[] raw,
            KissFrame frame
        ) => new(at, kind, raw) { Port = frame.Port, Command = frame.Command };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsStream)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }

        _writeLock.Dispose();
    }
}
