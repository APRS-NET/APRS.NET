using System.Net;
using AprsNet.Gateway;

namespace AprsNet.AprsIsCli.Tests;

public sealed class HealthTests
{
    [Theory]
    [InlineData(true, 0, "\"status\":\"healthy\"")]
    [InlineData(false, 1, "\"status\":\"unhealthy\"")]
    public async Task HealthPrintsTheReportAndExitsByStatus(bool connected, int exitCode, string status)
    {
        await using var server = new AprsIsServer(
            new AprsIsServerOptions { ServerName = "T2TEST", EndPoint = new IPEndPoint(IPAddress.Loopback, 0) }
        );
        server.Start();
        await using var hub = new AprsWebSocketHub(server);
        var health = new GatewayHealth();
        health.AddSource("tnc", () => connected);
        await using var web = new GatewayWebServer(
            hub,
            new GatewayWebServerOptions { EndPoint = new(IPAddress.Loopback, 0), Health = health }
        );
        web.Start();

        var result = await new CliHarness().RunAsync(
            ["health", $"http://127.0.0.1:{web.LocalEndPoint!.Port}/health"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Contains(status, result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoAnswerIsAConnectionFailure()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var result = await new CliHarness().RunAsync(
            ["health", $"http://127.0.0.1:{port}/health"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("no answer from", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyHttpUrlsAreAccepted()
    {
        var result = await new CliHarness().RunAsync(
            ["health", "ftp://example.org/health"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(3, result.ExitCode);
    }
}
