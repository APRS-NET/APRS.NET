using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>TLS logins against a local server with a test certificate.</summary>
public sealed class TlsTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LogsInAndReadsOverTls()
    {
        using var certificate = Certificate("localhost");
        await using var server = TlsServer.Start(certificate);
        await using var client = new AprsIsClient(Options(server, Trust(certificate)));

        var login = await client.ConnectAsync(Token).WaitAsync(Wait, Token);
        await using var lines = client.ReadLinesAsync(Token).GetAsyncEnumerator(Token);

        Assert.Equal("T2TLS", login.Server);
        Assert.True(await lines.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        Assert.Equal("N1ABC>APRS:>over TLS", lines.Current);
        Assert.StartsWith("user N0CALL pass -1 vers ", await server.LoginLine.WaitAsync(Wait, Token));
    }

    [Fact]
    public async Task AnUntrustedCertificateIsRejectedByDefault()
    {
        using var certificate = Certificate("localhost");
        await using var server = TlsServer.Start(certificate);
        await using var client = new AprsIsClient(Options(server));

        var error = await Assert.ThrowsAsync<AprsIsTlsException>(() =>
            client.ConnectAsync(Token).WaitAsync(Wait, Token)
        );

        Assert.Equal(AprsIsTlsFailure.CertificateRejected, error.Failure);
        Assert.True(error.PolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.IsAssignableFrom<IOException>(error);
    }

    [Fact]
    public async Task ACertificateForAnotherHostIsRejected()
    {
        using var certificate = Certificate("aprs.example.org");
        await using var server = TlsServer.Start(certificate);
        await using var client = new AprsIsClient(
            Options(server, (_, _, _, errors) => (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == 0)
        );

        var error = await Assert.ThrowsAsync<AprsIsTlsException>(() =>
            client.ConnectAsync(Token).WaitAsync(Wait, Token)
        );

        Assert.Equal(AprsIsTlsFailure.CertificateRejected, error.Failure);
        Assert.True(error.PolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact]
    public async Task AServerWithoutTlsIsAHandshakeFailure()
    {
        using var plain = new TcpListener(IPAddress.Loopback, 0);
        plain.Start();
        var serving = Task.Run(
            async () =>
            {
                using var tcp = await plain.AcceptTcpClientAsync(Token);
                await tcp.GetStream().WriteAsync("# aprsc 2.1.test\r\n"u8.ToArray(), Token);
                await Task.Delay(Wait, Token);
            },
            Token
        );
        var options = new AprsIsClientOptions
        {
            Endpoint = new AprsIsEndpoint("localhost", ((IPEndPoint)plain.LocalEndpoint).Port),
            Callsign = "N0CALL",
            UseTls = true,
        };
        await using var client = new AprsIsClient(options);

        var error = await Assert.ThrowsAsync<AprsIsTlsException>(() =>
            client.ConnectAsync(Token).WaitAsync(Wait, Token)
        );

        Assert.Equal(AprsIsTlsFailure.HandshakeFailed, error.Failure);
        Assert.Equal(SslPolicyErrors.None, error.PolicyErrors);
    }

    [Fact]
    public async Task OffersTheClientCertificate()
    {
        using var certificate = Certificate("localhost");
        using var station = Certificate("N0CALL");
        await using var server = TlsServer.Start(certificate, askForClientCertificate: true);
        var options = Options(server, Trust(certificate));
        options.ClientCertificates = [station];
        await using var client = new AprsIsClient(options);

        await client.ConnectAsync(Token).WaitAsync(Wait, Token);

        Assert.Equal(station.Thumbprint, (await server.ClientCertificate.WaitAsync(Wait, Token))?.GetCertHashString());
    }

    private static AprsIsClientOptions Options(
        TlsServer server,
        RemoteCertificateValidationCallback? validation = null
    ) =>
        new()
        {
            Endpoint = new AprsIsEndpoint("localhost", server.Port),
            Callsign = "N0CALL",
            UseTls = true,
            ServerCertificateValidation = validation,
        };

    /// <summary>Accepts exactly <paramref name="certificate"/>, as an app pinning a self-signed server would.</summary>
    private static RemoteCertificateValidationCallback Trust(X509Certificate2 certificate) =>
        (_, presented, _, _) => presented is not null && presented.GetCertHashString() == certificate.Thumbprint;

    private static X509Certificate2 Certificate(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(name);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")],
                critical: false
            )
        );
        using var created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        // Windows needs the key in a store-backed form to use it in a TLS handshake.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
    }

    /// <summary>One TLS connection: sends the banner, reads the login, accepts it and sends a packet.</summary>
    private sealed class TlsServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly TaskCompletionSource<string?> _login = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<X509Certificate?> _clientCertificate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly CancellationTokenSource _stopping = new();
        private Task _serving = Task.CompletedTask;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public Task<string?> LoginLine => _login.Task;

        public Task<X509Certificate?> ClientCertificate => _clientCertificate.Task;

        public static TlsServer Start(X509Certificate2 certificate, bool askForClientCertificate = false)
        {
            var server = new TlsServer();
            server._listener.Start();
            server._serving = server.ServeAsync(certificate, askForClientCertificate);
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            try
            {
                await _serving;
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException)
            {
                // The client went away or the test ended.
            }

            _listener.Dispose();
            _stopping.Dispose();
        }

        private async Task ServeAsync(X509Certificate2 certificate, bool askForClientCertificate)
        {
            using var tcp = await _listener.AcceptTcpClientAsync(_stopping.Token);
            // The station's certificate is self-signed, so only its chain fails; without one, none is offered.
            await using var ssl = new SslStream(
                tcp.GetStream(),
                false,
                (_, _, _, errors) =>
                    errors
                        is SslPolicyErrors.None
                            or SslPolicyErrors.RemoteCertificateChainErrors
                            or SslPolicyErrors.RemoteCertificateNotAvailable
            );
            try
            {
                await ssl.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        ClientCertificateRequired = askForClientCertificate,
                    },
                    _stopping.Token
                );
            }
            catch (Exception e) when (e is System.Security.Authentication.AuthenticationException or IOException)
            {
                _login.TrySetResult(null);
                return;
            }

            _clientCertificate.TrySetResult(ssl.RemoteCertificate);
            await ssl.WriteAsync("# aprsc 2.1.test\r\n"u8.ToArray(), _stopping.Token);
            using var reader = new StreamReader(ssl, Encoding.Latin1, leaveOpen: true);
            _login.TrySetResult(await reader.ReadLineAsync(_stopping.Token));
            await ssl.WriteAsync(
                "# logresp N0CALL unverified, server T2TLS\r\nN1ABC>APRS:>over TLS\r\n"u8.ToArray(),
                _stopping.Token
            );
            await Task.Delay(Timeout.Infinite, _stopping.Token);
        }
    }
}
