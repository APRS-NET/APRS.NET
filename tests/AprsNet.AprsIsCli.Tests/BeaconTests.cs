using AprsNet.AprsIsTcpClient;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.AprsIsCli.Tests;

/// <summary><c>aprscli send beacon</c> on a fake clock, at APRS101's example position.</summary>
public class BeaconTests
{
    private const string Callsign = "KD9XYZ-9";

    private static readonly string[] Position = ["-c", Callsign, "--lat", "49.0583", "--lon", "-72.0292"];

    private static (CliHarness Harness, FakeTimeProvider Clock) Verified()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
        var harness = new CliHarness { Clock = clock };
        harness.Environment["APRS_PASSCODE"] = AprsIsPasscode
            .Compute(Callsign)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        harness.Transport.Server("# aprsc 2.1.test");
        harness.Transport.Server($"# logresp {Callsign} verified, server T2TEST");
        return (harness, clock);
    }

    /// <summary>
    /// Runs the beacon, moving the clock on 30 seconds at a time, with the server's keepalive at each step as a real
    /// server sends one every 20 seconds (a silent server is dropped after 90).
    /// </summary>
    private static async Task<CliResult> Run(CliHarness harness, FakeTimeProvider clock, params string[] args)
    {
        var run = harness.RunAsync(["send", "beacon", .. Position, .. args], TestContext.Current.CancellationToken);
        for (var i = 0; i < 1000 && !run.IsCompleted; i++)
        {
            harness.Transport.Server("# aprsc 2.1.test 21 Oct 2026 12:00:00 GMT T2TEST 127.0.0.1:14580");
            clock.Advance(TimeSpan.FromSeconds(30));
            await Task.WhenAny(run, Task.Delay(20, TestContext.Current.CancellationToken));
        }

        return await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static string[] Times(CliResult result) =>
        [.. result.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[11..19])];

    [Fact]
    public async Task AFixedScheduleBeaconsAtOnceThenEveryInterval()
    {
        var (harness, clock) = Verified();

        var result = await Run(harness, clock, "--every", "10m", "--count", "3", "--comment", "Test");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["12:00:00", "12:10:00", "12:20:00"], Times(result));
        Assert.EndsWith(
            "KD9XYZ-9>APZNET:!4903.50N/07201.75W-Test",
            result.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0],
            StringComparison.Ordinal
        );
        Assert.Equal(
            3,
            harness.Transport.Written.Count(line => line.StartsWith(Callsign + ">", StringComparison.Ordinal))
        );
        Assert.Contains("sent 3 beacons.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADecayScheduleDoublesItsWaitUpToEvery()
    {
        var (harness, clock) = Verified();

        var result = await Run(harness, clock, "--decay", "1m", "--every", "4m", "--count", "5");

        Assert.Equal(["12:00:00", "12:01:00", "12:03:00", "12:07:00", "12:11:00"], Times(result));
    }

    [Fact]
    public async Task AStatusGoesWithEachBeacon()
    {
        var (harness, clock) = Verified();

        var result = await Run(harness, clock, "--every", "5m", "--count", "2", "--status", "On the air");

        Assert.Equal(["12:00:00", "12:00:00", "12:05:00", "12:05:00"], Times(result));
        Assert.EndsWith(
            "KD9XYZ-9>APZNET:>On the air",
            result.Out.Split(Environment.NewLine)[1],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ItStopsAfterTheDuration()
    {
        var (harness, clock) = Verified();

        var result = await Run(harness, clock, "--every", "10m", "-d", "25m");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["12:00:00", "12:10:00", "12:20:00"], Times(result));
    }

    [Fact]
    public async Task AnUnverifiedLoginSendsNothing()
    {
        var clock = new FakeTimeProvider();
        var harness = new CliHarness { Clock = clock };
        harness.Environment["APRS_PASSCODE"] = "1";
        harness.Transport.Server("# aprsc 2.1.test");
        harness.Transport.Server($"# logresp {Callsign} unverified, server T2TEST");

        var result = await Run(harness, clock, "--count", "1");

        Assert.Equal(4, result.ExitCode);
        Assert.Empty(result.Out);
    }

    [Fact]
    public async Task DryRunPrintsWhatEachBeaconSends()
    {
        var result = await new CliHarness().RunAsync(
            ["send", "beacon", .. Position, "--status", "Hi", "--dry-run"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            $"KD9XYZ-9>APZNET:!4903.50N/07201.75W-{Environment.NewLine}KD9XYZ-9>APZNET:>Hi{Environment.NewLine}",
            result.Out
        );
    }

    [Theory]
    [InlineData("--every", "30s", "--every '30s' is not valid")]
    [InlineData("--decay", "45m", "--decay '45m' is not valid")]
    [InlineData("--count", "0", "--count must be at least 1")]
    [InlineData("--duration", "forever", "--duration 'forever' is not valid")]
    public async Task BadScheduleOptionsAreRefused(string option, string value, string message)
    {
        var result = await new CliHarness().RunAsync(
            ["send", "beacon", .. Position, option, value],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TncSendHasNoBeacon()
    {
        var result = await new CliHarness().RunAsync(
            ["tnc", "send", "beacon", "-c", Callsign],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
    }
}
