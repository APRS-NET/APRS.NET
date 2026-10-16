using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AprsNet.AprsIsTcpClient;

/// <summary>TCP transport for APRS-IS, optionally over TLS.</summary>
public sealed class TcpAprsTransport : IAprsTransport
{
    private readonly AprsIsEndpoint _endpoint;
    private readonly TimeSpan _connectTimeout;
    private readonly int _maxLineLength;
    private readonly ILogger _logger;
    private readonly AprsIsClientOptions? _tls;
    private TcpClient? _client;
    private Stream? _stream;
    private LineReader? _reader;

    /// <summary>Creates a plain TCP transport; nothing connects until <see cref="ConnectAsync"/>.</summary>
    public TcpAprsTransport(AprsIsEndpoint endpoint, TimeSpan connectTimeout, int maxLineLength, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _endpoint = endpoint;
        _connectTimeout = connectTimeout;
        _maxLineLength = maxLineLength;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Creates a transport for <paramref name="options"/>: its endpoint, connect timeout and line length, over TLS when
    /// <see cref="AprsIsClientOptions.UseTls"/> is set. Nothing connects until <see cref="ConnectAsync"/>.
    /// </summary>
    public TcpAprsTransport(AprsIsClientOptions options, ILogger? logger)
        : this(
            (options ?? throw new ArgumentNullException(nameof(options))).Endpoint,
            options.ConnectTimeout,
            options.MaxLineLength,
            logger
        )
    {
        _tls = options.UseTls ? options : null;
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
        try
        {
            _stream = _tls is null
                ? client.GetStream()
                : await AuthenticateAsync(client, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"Timed out setting up TLS with {_endpoint}.");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _reader = new LineReader(_stream, _maxLineLength, _logger);
    }

    private async Task<Stream> AuthenticateAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var tls = _tls!;
        var errors = SslPolicyErrors.None;
        var ssl = new SslStream(
            client.GetStream(),
            leaveInnerStreamOpen: false,
            (sender, certificate, chain, policyErrors) =>
            {
                errors = policyErrors;
                return tls.ServerCertificateValidation is { } validate
                    ? validate(sender, certificate, chain, policyErrors)
                    : policyErrors == SslPolicyErrors.None;
            }
        );
        try
        {
            await ssl.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = _endpoint.Host,
                        ClientCertificates = tls.ClientCertificates,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            return ssl;
        }
        catch (AuthenticationException e)
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw errors != SslPolicyErrors.None
                ? new AprsIsTlsException(
                    AprsIsTlsFailure.CertificateRejected,
                    errors,
                    $"The certificate of {_endpoint} was not accepted ({errors}).",
                    e
                )
                : new AprsIsTlsException(
                    AprsIsTlsFailure.HandshakeFailed,
                    SslPolicyErrors.None,
                    $"TLS with {_endpoint} failed: {e.Message}",
                    e
                );
        }
        catch (IOException e) when (e is not AprsIsTlsException)
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw new AprsIsTlsException(
                AprsIsTlsFailure.HandshakeFailed,
                SslPolicyErrors.None,
                $"TLS with {_endpoint} failed: {e.Message}",
                e
            );
        }
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
