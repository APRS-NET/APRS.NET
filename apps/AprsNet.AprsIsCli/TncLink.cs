using System.Net.Sockets;
using AprsNet.Ax25;
using AprsNet.Kiss;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli;

/// <summary>
/// A single connection to a TNC that ends when the link does, over TCP or a serial port: for <c>tnc listen</c>
/// without <c>--reconnect</c> and for <c>tnc send</c>.
/// </summary>
internal sealed class TncLink : IAsyncDisposable
{
    private readonly KissTcpClient? _tcp;
    private readonly KissStreamClient? _serialKiss;

    private TncLink(KissTcpClient tcp)
    {
        _tcp = tcp;
        tcp.Warning += (_, e) => Warning?.Invoke(this, e);
    }

    private TncLink(KissStreamClient serialKiss)
    {
        _serialKiss = serialKiss;
        serialKiss.Warning += (_, e) => Warning?.Invoke(this, e);
    }

    /// <inheritdoc cref="KissStreamClient.Warning"/>
    public event EventHandler<KissWarningEventArgs>? Warning;

    /// <summary>Whether <paramref name="error"/> is how a TNC that can't be reached or went away fails.</summary>
    public static bool IsLinkFailure(Exception error) =>
        error is SocketException or TimeoutException or IOException or UnauthorizedAccessException;

    /// <summary>Connects to the TNC, or opens its serial port.</summary>
    /// <exception cref="Exception">One that <see cref="IsLinkFailure"/> accepts, when the TNC can't be reached.</exception>
    public static async Task<TncLink> OpenAsync(
        TncAddress address,
        int radioPort,
        KissTransmitLimit? transmitLimit,
        CancellationToken cancellationToken
    )
    {
        if (address.SerialPort is { } name)
        {
            // Opened as KissSerialConnection opens a port, so an unplug is a lost link here too.
            var serial = KissSerialConnection.OpenPort(name, address.BaudRate);
            return new TncLink(new KissStreamClient(serial, radioPort, ownsStream: true, transmitLimit));
        }

        var tcp = new KissTcpClient(
            new KissTcpOptions
            {
                Host = address.Host,
                Port = address.Port,
                TncPort = radioPort,
                TransmitLimit = transmitLimit,
            }
        );
        try
        {
            await tcp.ConnectAsync(cancellationToken);
        }
        catch
        {
            await tcp.DisposeAsync();
            throw;
        }

        return new TncLink(tcp);
    }

    /// <inheritdoc cref="KissStreamClient.ReadPacketsAsync"/>
    public IAsyncEnumerable<AprsPacket> ReadPacketsAsync(CancellationToken cancellationToken) =>
        _tcp?.ReadPacketsAsync(cancellationToken) ?? _serialKiss!.ReadPacketsAsync(cancellationToken);

    /// <inheritdoc cref="KissStreamClient.SendFrameAsync"/>
    public ValueTask SendFrameAsync(Ax25Frame frame, CancellationToken cancellationToken) =>
        _tcp?.SendFrameAsync(frame, cancellationToken) ?? _serialKiss!.SendFrameAsync(frame, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_tcp is not null)
        {
            await _tcp.DisposeAsync();
        }

        if (_serialKiss is not null)
        {
            await _serialKiss.DisposeAsync();
        }
    }
}
