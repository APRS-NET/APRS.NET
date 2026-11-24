using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.AprsIsCli.Tests;

/// <summary>
/// <c>listen --health-file</c>, <c>serve --health-file</c> and <c>health --file</c>: the liveness file one container
/// <c>HEALTHCHECK</c> uses for both commands.
/// </summary>
public sealed class HealthFileTests : IDisposable
{
    private static readonly DateTimeOffset _start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "aprscli-health-" + Guid.NewGuid().ToString("N")
    );

    public HealthFileTests()
    {
        Directory.CreateDirectory(_directory);
    }

    private string HealthPath => Path.Combine(_directory, "aprscli.health");

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    private static string Stamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The time written in the health file, once it holds <paramref name="expected"/>; fails after 10 s.</summary>
    private async Task WaitForStampAsync(DateTimeOffset expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var seen = "(no file)";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                seen = HealthFile.ReadText(HealthPath).Trim(); // shared as a health check reads it
                if (seen == Stamp(expected))
                {
                    return;
                }
            }
            catch (IOException)
            {
                // Not written yet.
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"The health file never held {Stamp(expected)}; last seen: {seen}");
    }

    private string ReadStamp() => File.Exists(HealthPath) ? HealthFile.ReadText(HealthPath).Trim() : "(no file)";

    [Fact]
    public async Task ListenWritesTheFileOnLoginAndRemovesItAtTheEnd()
    {
        var token = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(_start);
        var harness = new CliHarness { Clock = clock }.LoginAccepted();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var run = harness.RunAsync(["listen", "-c", "NZ0P", "--health-file", HealthPath], stop.Token);

        await WaitForStampAsync(_start);

        await stop.CancelAsync();
        Assert.Equal(0, (await run.WaitAsync(TimeSpan.FromSeconds(10), token)).ExitCode);
        Assert.False(File.Exists(HealthPath)); // a stopped listen is not alive
    }

    [Fact]
    public async Task ListenKeepsTheFileCurrentWithKeepalivesWhenNoPacketArrives()
    {
        var token = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(_start);
        var harness = new CliHarness { Clock = clock }.LoginAccepted();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var run = harness.RunAsync(["listen", "-c", "NZ0P", "--health-file", HealthPath], stop.Token);
        await WaitForStampAsync(_start);
        await harness.Transport.WaitUntilReadingAsync();

        // A quiet filter: only the server's keepalive, 20 s later.
        clock.Advance(TimeSpan.FromSeconds(20));
        harness.Transport.Server("# aprsc 2.1.test 17 Nov 2026 12:00:20 GMT T2TEST 1.2.3.4:14580");
        await WaitForStampAsync(_start.AddSeconds(20));

        // Then a packet, 20 s after that.
        clock.Advance(TimeSpan.FromSeconds(20));
        harness.Transport.Server("W1AW>APRS:>one");
        await WaitForStampAsync(_start.AddSeconds(40));

        await stop.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    [Fact]
    public async Task ListenWritesAtMostEveryFewSeconds()
    {
        var token = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(_start);
        var harness = new CliHarness { Clock = clock }.LoginAccepted();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var run = harness.RunAsync(["listen", "-c", "NZ0P", "--health-file", HealthPath], stop.Token);
        await WaitForStampAsync(_start);
        await harness.Transport.WaitUntilReadingAsync();

        clock.Advance(TimeSpan.FromSeconds(1));
        harness.Transport.Server("W1AW>APRS:>one");
        await harness.Transport.WaitUntilReadingAsync();
        await Task.Delay(100, token);
        Assert.Equal(Stamp(_start), ReadStamp()); // a busy feed doesn't rewrite the file for every line

        await stop.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    [Fact]
    public async Task ListenDoesNotTouchTheFileWhileReconnecting()
    {
        var token = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(_start);
        var harness = new CliHarness { Clock = clock }.LoginAccepted();
        var retry = new AprsNet.AprsIsTcpClient.Tests.FakeAprsTransport();
        retry.Server("# aprsc 2.1.test"); // the banner, with the login never answered
        harness.Reconnects.Enqueue(retry);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var run = harness.RunAsync(["listen", "-c", "NZ0P", "--reconnect", "--health-file", HealthPath], stop.Token);
        await WaitForStampAsync(_start);
        await harness.Transport.WaitUntilReadingAsync();

        clock.Advance(TimeSpan.FromSeconds(30));
        harness.Transport.Close();
        await Task.Delay(100, token);

        // Past the first retry (about 10 ms): the new connection reads the banner and waits for its login answer.
        clock.Advance(TimeSpan.FromSeconds(1));
        await retry.WaitUntilReadingAsync();
        await Task.Delay(100, token);
        Assert.Equal(Stamp(_start), ReadStamp());

        await stop.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    [Fact]
    public async Task ListenUsesTheEnvironmentVariableWithoutTheOption()
    {
        var token = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(_start);
        var harness = new CliHarness { Clock = clock }.LoginAccepted();
        harness.Environment[HealthFile.Variable] = HealthPath;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var run = harness.RunAsync(["listen", "-c", "NZ0P"], stop.Token);

        await WaitForStampAsync(_start);

        await stop.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    [Fact]
    public async Task ListenRemovesAFileLeftByAnEarlierRunBeforeItLogsIn()
    {
        var token = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(_start);
        await File.WriteAllTextAsync(HealthPath, Stamp(_start) + "\n", token); // a killed run's, still fresh
        var harness = new CliHarness { Clock = clock }; // the server never answers the login
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var run = harness.RunAsync(["listen", "-c", "NZ0P", "--health-file", HealthPath], stop.Token);

        await harness.Transport.WaitUntilReadingAsync();
        Assert.False(File.Exists(HealthPath));

        await stop.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    [Fact]
    public async Task ListenWithoutALoginLeavesNoFile()
    {
        var harness = new CliHarness { ConnectFailure = SocketFailures.Refused() };

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--health-file", HealthPath],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.ExitCode);
        Assert.False(File.Exists(HealthPath));
    }

    [Fact]
    public async Task ServeWritesTheFileWhileASourceIsUp()
    {
        var token = TestContext.Current.CancellationToken;
        using var tnc = new TcpListener(IPAddress.Loopback, 0);
        tnc.Start();
        var clock = new FakeTimeProvider(_start);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var run = ServeAsync(clock, ((IPEndPoint)tnc.LocalEndpoint).Port, stop.Token);

        using var radio = await tnc.AcceptFromAsync(run, token);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!File.Exists(HealthPath) && DateTime.UtcNow < deadline)
        {
            clock.Advance(HealthFile.RefreshInterval);
            await Task.Delay(20, token);
        }

        Assert.True(File.Exists(HealthPath));

        await stop.CancelAsync();
        Assert.Equal(0, (await run.WaitAsync(TimeSpan.FromSeconds(10), token)).ExitCode);
        Assert.False(File.Exists(HealthPath));
    }

    [Fact]
    public async Task ServeWritesNoFileWhileNoSourceIsUp()
    {
        var token = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(_start);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var run = ServeAsync(clock, FreePort(), stop.Token); // nothing listens: the TNC never connects

        for (var i = 0; i < 10; i++)
        {
            clock.Advance(HealthFile.RefreshInterval);
            await Task.Delay(20, token);
        }

        Assert.False(File.Exists(HealthPath));

        await stop.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    private Task<CliResult> ServeAsync(FakeTimeProvider clock, int tncPort, CancellationToken stop) =>
        new CliHarness { Clock = clock }.RunAsync(
            [
                "serve",
                "--tnc",
                $"127.0.0.1:{tncPort}",
                "--listen",
                $"127.0.0.1:{FreePort()}",
                "--health-file",
                HealthPath,
            ],
            stop
        );

    [Theory]
    [InlineData(0, 0)]
    [InlineData(89, 0)]
    [InlineData(91, 1)]
    public async Task HealthFileIsHealthyUntilMaxAge(int ageSeconds, int exitCode)
    {
        var clock = new FakeTimeProvider(_start);
        await File.WriteAllTextAsync(
            HealthPath,
            Stamp(_start.AddSeconds(-ageSeconds)) + "\n",
            TestContext.Current.CancellationToken
        );

        var result = await new CliHarness { Clock = clock }.RunAsync(
            ["health", "--file", HealthPath],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Contains(
            exitCode == 0 ? "healthy: last alive" : "unhealthy: last alive",
            exitCode == 0 ? result.Out : result.Error,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task HealthFileTakesMaxAge()
    {
        var clock = new FakeTimeProvider(_start);
        await File.WriteAllTextAsync(
            HealthPath,
            Stamp(_start.AddMinutes(-3)) + "\n",
            TestContext.Current.CancellationToken
        );

        var stale = await new CliHarness { Clock = clock }.RunAsync(
            ["health", "--file", HealthPath],
            TestContext.Current.CancellationToken
        );
        var fresh = await new CliHarness { Clock = clock }.RunAsync(
            ["health", "--file", HealthPath, "--max-age", "5m"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, stale.ExitCode);
        Assert.Contains("older than --max-age 90s", stale.Error, StringComparison.Ordinal);
        Assert.Equal(0, fresh.ExitCode);
    }

    [Fact]
    public async Task AMissingHealthFileIsUnhealthy()
    {
        var result = await new CliHarness().RunAsync(
            ["health", "--file", HealthPath],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("no health file at", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHealthFileWithoutATimeUsesItsModificationTime()
    {
        var clock = new FakeTimeProvider(_start);
        await File.WriteAllTextAsync(HealthPath, "", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(HealthPath, _start.AddSeconds(-10).UtcDateTime);

        var result = await new CliHarness { Clock = clock }.RunAsync(
            ["health", "--file", HealthPath],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("(10s ago)", result.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--max-age", "soon")]
    [InlineData("http://127.0.0.1:14590/health", null)]
    public async Task BadHealthFileOptionsAreUsageErrors(string extra, string? value)
    {
        string[] args = value is null
            ? ["health", "--file", HealthPath, extra]
            : ["health", "--file", HealthPath, extra, value];

        var result = await new CliHarness().RunAsync(args, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
    }
}
