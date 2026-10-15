using System.Net.Sockets;
using System.Runtime.CompilerServices;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss;

/// <summary>
/// A connection to a KISS TNC over TCP, such as Dire Wolf or a soundmodem on port 8001: reads the AX.25 frames the
/// TNC hears and sends APRS packets for it to transmit. Single-use: after the TNC closes the connection, create a new
/// client.
/// </summary>
public sealed class KissTcpClient : IAsyncDisposable
{
    /// <summary>The usual KISS TCP port (Dire Wolf's default).</summary>
    public const int DefaultPort = 8001;

    private readonly string _host;
    private readonly int _port;
    private readonly int _tncPort;
    private readonly TimeSpan _connectTimeout;
    private readonly KissDecoder _decoder = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private int _disposed;

    /// <summary>Creates a client; nothing connects until <see cref="ConnectAsync"/>.</summary>
    /// <param name="host">The TNC's host name or address.</param>
    /// <param name="port">The TNC's KISS TCP port.</param>
    /// <param name="tncPort">
    /// The radio port on a multi-port TNC, 0 to 15: frames are sent there, and only frames heard there are read.
    /// </param>
    /// <param name="connectTimeout">How long to wait for the connection; 10 seconds when not given.</param>
    public KissTcpClient(string host, int port = DefaultPort, int tncPort = 0, TimeSpan? connectTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentOutOfRangeException.ThrowIfNegative(tncPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tncPort, 15);
        _host = host;
        _port = port;
        _tncPort = tncPort;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>KISS frames dropped for an invalid escape or for being too long: a sign of a noisy serial link.</summary>
    public long DroppedFrames => _decoder.DroppedFrames;

    /// <summary>Connects to the TNC.</summary>
    /// <exception cref="TimeoutException">The TNC did not accept the connection in time.</exception>
    /// <exception cref="SocketException">The TNC could not be reached.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_client is not null)
        {
            throw new InvalidOperationException("This client is already connected.");
        }

        var client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_connectTimeout);
        try
        {
            await client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"Timed out connecting to the TNC at {_host}:{_port}.");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _stream = client.GetStream();
    }

    /// <summary>
    /// Yields every AX.25 frame the TNC passes on from its radio port, APRS or not, until the TNC closes the
    /// connection. Frames that are not valid AX.25 are skipped. Intended for a single consumer.
    /// </summary>
    public async IAsyncEnumerable<Ax25Frame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var stream = _stream ?? throw new InvalidOperationException("Connect before reading.");
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
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
    /// the TNC closes the connection. Use this or <see cref="ReadFramesAsync"/>, not both.
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
    /// Sends <paramref name="packet"/> for the TNC to transmit on its radio port. The TNC handles channel access; it
    /// may hold the frame until the channel is clear.
    /// </summary>
    /// <exception cref="ArgumentException">The packet cannot be sent as AX.25; see <see cref="Ax25Frame.FromAprsPacket"/>.</exception>
    public ValueTask SendAsync(AprsPacket packet, CancellationToken cancellationToken = default) =>
        SendFrameAsync(Ax25Frame.FromAprsPacket(packet), cancellationToken);

    /// <summary>Sends an AX.25 frame for the TNC to transmit on its radio port.</summary>
    public async ValueTask SendFrameAsync(Ax25Frame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var stream = _stream ?? throw new InvalidOperationException("Connect before sending.");
        var bytes = KissFrame.ForAx25(frame, _tncPort).Encode();
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _client?.Dispose();
            _writeLock.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
