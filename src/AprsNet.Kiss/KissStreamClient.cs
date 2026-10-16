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

    /// <summary>KISS frames dropped for an invalid escape or for being too long: a sign of a noisy serial link.</summary>
    public long DroppedFrames => _decoder.DroppedFrames;

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

            foreach (var kiss in _decoder.Decode(buffer.AsSpan(0, read)))
            {
                if (kiss.Port == _tncPort && kiss.TryGetAx25(out var frame))
                {
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
        }
        finally
        {
            _writeLock.Release();
        }
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
