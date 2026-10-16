using System.Net.Sockets;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss;

/// <summary>
/// A connection to a KISS TNC over TCP, such as Dire Wolf or a soundmodem on port 8001: reads the AX.25 frames the
/// TNC hears and sends APRS packets for it to transmit. Single-use: after the TNC closes the connection, create a new
/// client. For a serial or Bluetooth TNC, open the stream and use <see cref="KissStreamClient"/>.
/// </summary>
public sealed class KissTcpClient : IAsyncDisposable
{
    /// <summary>The usual KISS TCP port (Dire Wolf's default).</summary>
    public const int DefaultPort = 8001;

    private readonly string _host;
    private readonly int _port;
    private readonly int _tncPort;
    private readonly TimeSpan _connectTimeout;
    private TcpClient? _client;
    private KissStreamClient? _kiss;
    private int _disposed;

    /// <summary>
    /// Creates a client for the TNC at <paramref name="host"/>:<paramref name="port"/> with the other settings at their
    /// defaults; nothing connects until <see cref="ConnectAsync"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The host is empty or the port is not 1 to 65535.</exception>
    public KissTcpClient(string host, int port = DefaultPort)
        : this(new KissTcpOptions { Host = host, Port = port }) { }

    /// <summary>Creates a client with <paramref name="options"/>; nothing connects until <see cref="ConnectAsync"/>.</summary>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public KissTcpClient(KissTcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _host = options.Host;
        _port = options.Port;
        _tncPort = options.TncPort;
        _connectTimeout = options.ConnectTimeout;
        TransmitLimit = options.CreateTransmitLimit();
    }

    /// <inheritdoc cref="KissStreamClient.TransmitLimit"/>
    public KissTransmitLimit TransmitLimit { get; }

    /// <summary>KISS frames dropped for an invalid escape or for being too long.</summary>
    public long DroppedFrames => _kiss?.DroppedFrames ?? 0;

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
        _kiss = new KissStreamClient(client.GetStream(), _tncPort, transmitLimit: TransmitLimit)
        {
            MetricsTnc = $"{_host}:{_port}",
        };
        KissMetrics.Active(_kiss.MetricsTnc, +1);
    }

    /// <inheritdoc cref="KissStreamClient.ReadFramesAsync"/>
    public IAsyncEnumerable<Ax25Frame> ReadFramesAsync(CancellationToken cancellationToken = default) =>
        Connected.ReadFramesAsync(cancellationToken);

    /// <inheritdoc cref="KissStreamClient.ReadPacketsAsync"/>
    public IAsyncEnumerable<AprsPacket> ReadPacketsAsync(CancellationToken cancellationToken = default) =>
        Connected.ReadPacketsAsync(cancellationToken);

    /// <inheritdoc cref="KissStreamClient.SendAsync"/>
    public ValueTask SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Connected.SendAsync(packet, cancellationToken);
    }

    /// <inheritdoc cref="KissStreamClient.SendFrameAsync"/>
    public ValueTask SendFrameAsync(Ax25Frame frame, CancellationToken cancellationToken = default) =>
        Connected.SendFrameAsync(frame, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_kiss is not null)
        {
            KissMetrics.Active(_kiss.MetricsTnc, -1);
            await _kiss.DisposeAsync().ConfigureAwait(false);
        }

        _client?.Dispose();
    }

    private KissStreamClient Connected =>
        _kiss ?? throw new InvalidOperationException("Connect before reading or sending.");
}
