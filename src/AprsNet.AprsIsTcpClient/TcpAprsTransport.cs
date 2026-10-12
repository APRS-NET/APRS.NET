using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AprsNet.AprsIsTcpClient;

/// <summary>Plain TCP transport for APRS-IS.</summary>
public sealed class TcpAprsTransport : IAprsTransport
{
    private readonly AprsIsEndpoint _endpoint;
    private readonly TimeSpan _connectTimeout;
    private readonly int _maxLineLength;
    private readonly ILogger _logger;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private LineReader? _reader;

    /// <summary>Creates a transport; nothing connects until <see cref="ConnectAsync"/>.</summary>
    public TcpAprsTransport(AprsIsEndpoint endpoint, TimeSpan connectTimeout, int maxLineLength, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _endpoint = endpoint;
        _connectTimeout = connectTimeout;
        _maxLineLength = maxLineLength;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_connectTimeout);
        try
        {
            await client.ConnectAsync(_endpoint.Host, _endpoint.Port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"Timed out connecting to {_endpoint}.");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _stream = client.GetStream();
        _reader = new LineReader(_stream, _maxLineLength, _logger);
    }

    /// <inheritdoc />
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        (_reader ?? throw new InvalidOperationException("Not connected.")).ReadLineAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("Not connected.");
        await stream.WriteAsync(Encoding.Latin1.GetBytes(line + "\r\n"), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }
}
