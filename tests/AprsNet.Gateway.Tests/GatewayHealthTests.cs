using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Gateway.Tests;

public sealed class GatewayHealthTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 7, 30, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(new bool[0], GatewayHealthStatus.Healthy)]
    [InlineData(new[] { true, true }, GatewayHealthStatus.Healthy)]
    [InlineData(new[] { true, false }, GatewayHealthStatus.Degraded)]
    [InlineData(new[] { false, false }, GatewayHealthStatus.Unhealthy)]
    public void TheStatusFollowsTheSources(bool[] connected, GatewayHealthStatus expected)
    {
        var health = new GatewayHealth(timeProvider: _time);
        for (var i = 0; i < connected.Length; i++)
        {
            var up = connected[i];
            health.AddSource($"source{i}", () => up);
        }

        var report = health.Check();
        Assert.Equal(expected, report.Status);
        Assert.Equal(connected.Length, report.Sources.Count);
        Assert.Equal(_time.GetUtcNow(), report.CheckedAt);
    }

    [Fact]
    public void ACheckThatThrowsIsNotConnected()
    {
        var health = new GatewayHealth();
        health.AddSource("tnc", () => throw new InvalidOperationException());
        Assert.Equal(GatewayHealthStatus.Unhealthy, health.Check().Status);
    }

    [Fact]
    public void AQuietSourceIsReportedButStillCountsAsConnected()
    {
        var quiet = true;
        var health = new GatewayHealth();
        health.AddSource("tnc", () => true, () => quiet);
        health.AddSource("aprs-is", () => true);
        health.AddSource("broken", () => true, () => throw new InvalidOperationException());

        var report = health.Check();
        Assert.Equal(GatewayHealthStatus.Healthy, report.Status);
        Assert.True(report.Sources["tnc"].IsQuiet);
        Assert.False(report.Sources["aprs-is"].IsQuiet);
        Assert.False(report.Sources["broken"].IsQuiet);

        quiet = false;
        Assert.False(health.Check().Sources["tnc"].IsQuiet);
    }

    [Fact]
    public void ASourceSpeakingTheWrongProtocolIsReportedAndIsNotUp()
    {
        var mismatch = true;
        var health = new GatewayHealth();
        health.AddSource("tnc", () => true, null, () => mismatch);

        var report = health.Check();
        Assert.Equal(GatewayHealthStatus.Unhealthy, report.Status);
        Assert.True(report.Sources["tnc"].IsConnected);
        Assert.True(report.Sources["tnc"].IsProtocolMismatch);

        health.AddSource("aprs-is", () => true);
        Assert.Equal(GatewayHealthStatus.Degraded, health.Check().Status);

        mismatch = false;
        report = health.Check();
        Assert.Equal(GatewayHealthStatus.Healthy, report.Status);
        Assert.False(report.Sources["tnc"].IsProtocolMismatch);
    }

    [Fact]
    public void SourcesAreAddedOnceAndCanBeRemoved()
    {
        var health = new GatewayHealth();
        health.AddSource("tnc", () => true);
        Assert.Throws<ArgumentException>(() => health.AddSource("tnc", () => true));
        Assert.True(health.RemoveSource("tnc"));
        Assert.False(health.RemoveSource("tnc"));
        Assert.Empty(health.Check().Sources);
    }

    [Fact]
    public async Task TheFeedSaysWhenEachSourceLastDeliveredAPacket()
    {
        await using var server = Server();
        await using var feed = new GatewayFeed(server, timeProvider: _time);
        var health = new GatewayHealth(feed, _time);
        health.AddSource("tnc", () => true);
        health.AddSource("aprs-is", () => true);
        Assert.Null(health.Check().Sources["tnc"].LastPacket);

        await feed.AddSource("tnc", Packets("N0CALL>APRS:>one")).WaitAsync(Wait, TestContext.Current.CancellationToken);
        var deadline = DateTime.UtcNow + Wait;
        while (feed.Published == 0)
        {
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        var report = health.Check();
        Assert.Equal(_time.GetUtcNow(), report.Sources["tnc"].LastPacket);
        Assert.Null(report.Sources["aprs-is"].LastPacket);
    }

    [Theory]
    [InlineData(true, "200", "degraded")]
    [InlineData(false, "503", "unhealthy")]
    public async Task TheWebServerAnswersHealthChecks(bool oneUp, string status, string word)
    {
        await using var server = Server();
        await using var hub = new AprsWebSocketHub(server);
        var health = new GatewayHealth(timeProvider: _time);
        health.AddSource("aprs-is", () => oneUp);
        health.AddSource("tnc", () => false, () => true, () => true);
        await using var web = new GatewayWebServer(
            hub,
            new GatewayWebServerOptions { EndPoint = new(IPAddress.Loopback, 0), Health = health }
        );
        web.Start();

        var response = await GetAsync(web, "/health");
        Assert.StartsWith($"HTTP/1.1 {status} ", response, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/json", response, StringComparison.Ordinal);
        var body = JsonDocument
            .Parse(response[(response.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..])
            .RootElement;
        Assert.Equal(word, body.GetProperty("status").GetString());
        Assert.Equal("2026-10-15T07:30:00.000Z", body.GetProperty("checkedAt").GetString());
        Assert.Equal(0, body.GetProperty("clients").GetProperty("aprsIs").GetInt32());
        Assert.Equal(oneUp, body.GetProperty("sources").GetProperty("aprs-is").GetProperty("connected").GetBoolean());
        Assert.False(body.GetProperty("sources").GetProperty("aprs-is").GetProperty("quiet").GetBoolean());
        Assert.True(body.GetProperty("sources").GetProperty("tnc").GetProperty("quiet").GetBoolean());
        Assert.True(body.GetProperty("sources").GetProperty("tnc").GetProperty("protocolMismatch").GetBoolean());
        Assert.False(body.GetProperty("sources").GetProperty("aprs-is").GetProperty("protocolMismatch").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("sources").GetProperty("tnc").GetProperty("lastPacket").ValueKind
        );
    }

    [Fact]
    public async Task WithoutAHealthCheckThePathIsNotFound()
    {
        await using var server = Server();
        await using var hub = new AprsWebSocketHub(server);
        await using var web = new GatewayWebServer(
            hub,
            new GatewayWebServerOptions { EndPoint = new(IPAddress.Loopback, 0) }
        );
        web.Start();
        Assert.StartsWith("HTTP/1.1 404 ", await GetAsync(web, "/health"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheHealthPathMustDifferFromTheWebSocketPath() =>
        Assert.Throws<ArgumentException>(() => new GatewayWebServerOptions { HealthPath = "/aprs" }.Validate());

    private AprsIsServer Server()
    {
        var server = new AprsIsServer(
            new AprsIsServerOptions
            {
                ServerName = "T2TEST",
                EndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                TimeProvider = _time,
            }
        );
        server.Start();
        return server;
    }

    private static async IAsyncEnumerable<AprsPacket> Packets(params string[] lines)
    {
        foreach (var line in lines)
        {
            await Task.Yield();
            yield return AprsPacket.Parse(line);
        }
    }

    private static async Task<string> GetAsync(GatewayWebServer web, string path)
    {
        var token = TestContext.Current.CancellationToken;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(web.LocalEndPoint!, token);
        await tcp.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: x\r\n\r\n"), token);
        using var reader = new StreamReader(tcp.GetStream(), Encoding.UTF8);
        return await reader.ReadToEndAsync(token).WaitAsync(Wait, token);
    }
}
