using System.Runtime.CompilerServices;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss;

/// <summary>
/// Speaks KISS over any byte stream an app has already opened: a serial port's <c>BaseStream</c> for a hardware TNC,
/// a Bluetooth link, or a network stream. It reads the AX.25 frames the TNC hears and sends APRS packets for it to
/// transmit. <see cref="KissTcpClient"/> uses it for TCP.
/// </summary>
public sealed class KissStreamClient : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly int _tncPort;
    private readonly KissDecoder _decoder = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
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

    /// <summary>How fast frames may be sent; a send waits for its turn.</summary>
    public KissTransmitLimit TransmitLimit { get; }

    /// <summary>Called after each read that brought bytes, before they are decoded.</summary>
    internal Action? DataRead { get; init; }

    /// <summary>
    /// For a client inside another (a TCP client, a lasting connection) that forwards <see cref="Traffic"/>: whether
    /// anyone listens to the outer one, so the forwarding subscription alone doesn't turn tracing on.
    /// </summary>
    internal Func<bool>? WantsTraffic { get; set; }

    private bool Tracing => Traffic is not null && (WantsTraffic?.Invoke() ?? true);

    /// <summary>The clock for <see cref="KissTrafficEventArgs.At"/>.</summary>
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>The <c>tnc</c> tag on this client's metrics: the TNC's <c>host:port</c> when a TCP client made it.</summary>
    internal string MetricsTnc { get; init; } = KissMetrics.StreamTnc;

    /// <summary>KISS frames dropped for an invalid escape or for being too long: a sign of a noisy serial link.</summary>
    public long DroppedFrames => _decoder.DroppedFrames;

    /// <summary>Bytes read that were not part of a frame passed on; see <see cref="KissDecoder.DiscardedBytes"/>.</summary>
    public long DiscardedBytes => _decoder.DiscardedBytes;

    /// <summary>
    /// Fires when the TNC starts sending something other than KISS frames, and again when it stops; on the reading
    /// thread. See <see cref="KissWarning"/>.
    /// </summary>
    public event EventHandler<KissWarningEventArgs>? Warning;

    /// <summary>
    /// Everything that crosses the link, for a troubleshooting console: each frame received (APRS or not, for this
    /// port or another, damaged), bytes outside any frame a line at a time, and each frame sent. Received entries
    /// are raised on the reading thread, sent ones on the sending thread. Nothing extra is done while no one is
    /// subscribed; an exception thrown by a handler is ignored. See <see cref="KissTrafficKind"/>.
    /// </summary>
    public event EventHandler<KissTrafficEventArgs>? Traffic;

    /// <summary>
    /// Yields every AX.25 frame the TNC passes on from its radio port, APRS or not, until the stream ends. Frames that
    /// are not valid AX.25 are skipped. Intended for a single consumer.
    /// </summary>
    public async IAsyncEnumerable<Ax25Frame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var buffer = new byte[4096];
        while (true)
        {
            var read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                yield break;
            }

            DataRead?.Invoke();

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
                if (kiss.Port == _tncPort && kiss.TryGetAx25(out var frame))
                {
                    KissMetrics.Received(MetricsTnc);
                    yield return frame!;
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
    public async ValueTask SendFrameAsync(Ax25Frame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var bytes = KissFrame.ForAx25(frame, _tncPort).Encode();
        await TransmitLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            KissMetrics.Sent(MetricsTnc);
            if (Tracing)
            {
                Raise(
                    new KissTrafficEventArgs(TimeProvider.GetUtcNow(), KissTrafficKind.Sent, bytes)
                    {
                        Port = _tncPort,
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
            if (frame.Port != client._tncPort)
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
