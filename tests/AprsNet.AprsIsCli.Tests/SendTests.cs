using AprsNet.AprsIsTcpClient;
using AprsNet.AprsIsTcpClient.Tests;

namespace AprsNet.AprsIsCli.Tests;

public class SendTests
{
    private const string Callsign = "KD9XYZ-9";

    private static CliHarness Verified(string status = "verified", TimeProvider? clock = null)
    {
        var harness = new CliHarness { Clock = clock };
        harness.Environment["APRS_PASSCODE"] = AprsIsPasscode
            .Compute(Callsign)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        harness.Transport.Server("# aprsc 2.1.test");
        harness.Transport.Server($"# logresp {Callsign} {status}, server T2TEST");
        return harness;
    }

    private static async Task<List<string>> WaitForWritten(CliHarness harness, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            lock (harness.Transport.Written)
            {
                if (harness.Transport.Written.Count >= count)
                {
                    return [.. harness.Transport.Written];
                }
            }

            await Task.Delay(5, timeout.Token);
        }
    }

    [Fact]
    public async Task DryRunPrintsThePacketWithoutConnecting()
    {
        var harness = new CliHarness();

        var result = await harness.RunAsync(
            ["send", "status", "Testing", "-c", "kd9xyz-9", "--dry-run"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"KD9XYZ-9>APZNET:>Testing{Environment.NewLine}", result.Out);
        Assert.Equal(0, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task SendsAStatusOnAVerifiedLogin()
    {
        var harness = Verified();

        var result = await harness.RunAsync(
            ["send", "status", "Testing", "-c", Callsign, "--path", "WIDE1-1"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith($"user {Callsign} pass ", harness.Transport.Written[0], StringComparison.Ordinal);
        Assert.Equal("KD9XYZ-9>APZNET,WIDE1-1:>Testing", harness.Transport.Written[1]);
        Assert.Null(harness.ClientOptions!.Filter);
    }

    [Theory]
    [InlineData("", "ssl.aprs2.net:24580")]
    [InlineData("--server example.org", "example.org:24580")]
    [InlineData("--server example.org:1234", "example.org:1234")]
    public async Task SendsOverTls(string server, string expected)
    {
        var harness = Verified();

        var result = await harness.RunAsync(
            [
                "send",
                "status",
                "Testing",
                "-c",
                Callsign,
                "--tls",
                .. server.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            ],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.True(harness.ClientOptions!.UseTls);
        Assert.Equal(expected, harness.ClientOptions.Endpoint.ToString());
    }

    [Fact]
    public async Task TlsCannotUseARegion()
    {
        var result = await Verified()
            .RunAsync(
                ["send", "status", "Testing", "-c", Callsign, "--tls", "--region", "noam"],
                TestContext.Current.CancellationToken
            );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("--tls cannot be combined with --region", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendsAPositionAndAnObject()
    {
        var harness = Verified();

        var result = await harness.RunAsync(
            ["send", "object", "LEADER", "--lat", "49.05833", "--lon", "-72.02917", "--symbol", "/>", "-c", Callsign],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("KD9XYZ-9>APZNET:;LEADER   *111200z4903.50N/07201.75W>", harness.Transport.Written[1]);
    }

    [Fact]
    public async Task AMessageWaitsForTheAcknowledgement()
    {
        var harness = Verified();

        var run = harness.RunAsync(
            ["send", "message", "WU2Z", "Hello there", "-c", Callsign, "--wait-replies", "0"],
            TestContext.Current.CancellationToken
        );
        var written = await WaitForWritten(harness, 2);
        var id = written[1][(written[1].LastIndexOf('{') + 1)..];
        harness.Transport.Server($"WU2Z>APRS,TCPIP*,qAC,T2TEST::{Callsign} :ack{id}");
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Acknowledged by WU2Z after 1 attempt.", result.Out, StringComparison.Ordinal);
        Assert.Equal($"g/{Callsign}", harness.ClientOptions!.Filter);
    }

    [Fact]
    public async Task AMessageCarriesAReplyAckIdAndAReplyCanAcknowledgeIt()
    {
        var harness = Verified();

        var run = harness.RunAsync(
            ["send", "message", "WU2Z", "Hello there", "-c", Callsign, "--wait-replies", "0"],
            TestContext.Current.CancellationToken
        );
        var written = await WaitForWritten(harness, 2);
        Assert.Equal("KD9XYZ-9>APZNET::WU2Z     :Hello there{01}", written[1]);

        // A reply carrying the ack (APRS 1.1 reply-ack) delivers the message, and is itself acked.
        harness.Transport.Server($"WU2Z>APRS,TCPIP*,qAC,T2TEST::{Callsign} :Got it{{7}}01");
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Reply from WU2Z: Got it", result.Out, StringComparison.Ordinal);
        Assert.Contains("Acknowledged by WU2Z after 1 attempt.", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMessageNobodyAnswersFailsWhenItsRetriesRunOut()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var harness = Verified(clock: clock);

        var run = harness.RunAsync(
            ["send", "message", "WU2Z", "Hello there", "-c", Callsign, "--retry", "none", "--wait-replies", "0"],
            TestContext.Current.CancellationToken
        );
        await WaitForWritten(harness, 2);
        clock.Advance(TimeSpan.FromMinutes(2));
        var result = await run;

        Assert.Equal(5, result.ExitCode);
        Assert.Contains("no acknowledgement after 1 attempt.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownRetryProfileIsAUsageError()
    {
        var result = await Verified()
            .RunAsync(
                ["send", "message", "WU2Z", "Hello", "-c", Callsign, "--retry", "often"],
                TestContext.Current.CancellationToken
            );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("often", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMessageOutlastsADroppedConnection()
    {
        var harness = Verified();

        var run = harness.RunAsync(
            ["send", "message", "WU2Z", "Hello there", "-c", Callsign, "--wait-replies", "0"],
            TestContext.Current.CancellationToken
        );
        var written = await WaitForWritten(harness, 2);
        var second = new FakeAprsTransport();
        second.Server($"# logresp {Callsign} verified, server T2OTHER");
        second.Server(
            $"WU2Z>APRS,TCPIP*,qAC,T2OTHER::{Callsign} :ack{written[1][(written[1].LastIndexOf('{') + 1)..]}"
        );
        harness.Reconnects.Enqueue(second);
        harness.Transport.Close();
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Acknowledged by WU2Z after 1 attempt.", result.Out, StringComparison.Ordinal);
        Assert.Contains("closed the connection", result.Error, StringComparison.Ordinal);
        Assert.Contains("reconnecting to rotate.aprs2.net:14580", result.Error, StringComparison.Ordinal);
        Assert.Equal(2, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task AcknowledgesRepliesWhileWaitingForThem()
    {
        var harness = Verified();

        var run = harness.RunAsync(
            ["send", "message", "WU2Z", "Hello there", "-c", Callsign, "--wait-replies", "2s"],
            TestContext.Current.CancellationToken
        );
        var written = await WaitForWritten(harness, 2);
        harness.Transport.Server(
            $"WU2Z>APRS,TCPIP*,qAC,T2TEST::{Callsign} :ack{written[1][(written[1].LastIndexOf('{') + 1)..]}"
        );
        harness.Transport.Server($"WU2Z>APRS,TCPIP*,qAC,T2TEST::{Callsign} :Received{{7");
        harness.Transport.Server($"WU2Z>APRS,TCPIP*,qAC,T2TEST::{Callsign} :Received{{7"); // its retry
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Out.Split(Environment.NewLine), line => line == "Reply from WU2Z: Received");
        Assert.Equal(2, harness.Transport.Written.Count(line => line == "KD9XYZ-9>APZNET::WU2Z     :ack7"));
        Assert.Contains("Waiting 2s for replies", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectedMessageExitsWithNotDelivered()
    {
        var harness = Verified();

        var run = harness.RunAsync(
            ["send", "message", "WU2Z", "Hello there", "-c", Callsign, "--wait-replies", "0"],
            TestContext.Current.CancellationToken
        );
        var written = await WaitForWritten(harness, 2);
        harness.Transport.Server(
            $"WU2Z>APRS,TCPIP*,qAC,T2TEST::{Callsign} :rej{written[1][(written[1].LastIndexOf('{') + 1)..]}"
        );
        var result = await run;

        Assert.Equal(5, result.ExitCode);
        Assert.Contains("rejected", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnverifiedLoginSendsNothing()
    {
        var harness = Verified("unverified");

        var result = await harness.RunAsync(
            ["send", "status", "Testing", "-c", Callsign],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(4, result.ExitCode);
        Assert.Contains("did not accept the passcode", result.Error, StringComparison.Ordinal);
        Assert.Single(harness.Transport.Written);
    }

    [Theory]
    [InlineData(new[] { "send", "status", "Testing" }, "--callsign is required")]
    [InlineData(new[] { "send", "status", "Testing", "-c", "N0CALL-9" }, "placeholder")]
    [InlineData(new[] { "send", "status", "Testing", "-c", "KD9XYZ-9", "--tocall", "aprs!" }, "TOCALL")]
    [InlineData(new[] { "send", "position", "--lat", "49", "-c", "KD9XYZ-9" }, "--lat and --lon")]
    [InlineData(new[] { "send", "position", "--lat", "49", "--lon", "7", "--symbol", "x", "-c", "KD9XYZ" }, "--symbol")]
    [InlineData(new[] { "send", "message", "WU2Z", "a|b", "-c", "KD9XYZ-9" }, "'|'")]
    [InlineData(
        new[] { "send", "message", "WU2Z", "hi", "-c", "KD9XYZ-9", "--wait-replies", "soon" },
        "--wait-replies"
    )]
    public async Task RefusesUnusableInputWithoutConnecting(string[] args, string reason)
    {
        var harness = Verified();

        var result = await harness.RunAsync(args, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(reason, result.Error, StringComparison.Ordinal);
        Assert.Equal(0, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task NeedsAPasscodeToConnect()
    {
        var harness = new CliHarness();

        var result = await harness.RunAsync(
            ["send", "status", "Testing", "-c", Callsign],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("APRS_PASSCODE", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task WeatherWithAPositionIsACompleteReport()
    {
        var harness = Verified();

        var result = await harness.RunAsync(
            [
                "send",
                "weather",
                "--lat",
                "49.05833",
                "--lon",
                "-72.02917",
                "--wind-dir",
                "220",
                "--wind-speed",
                "4",
                "--temp",
                "77",
                "--humidity",
                "50",
                "--pressure",
                "1013.2",
                "-c",
                Callsign,
            ],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("KD9XYZ-9>APZNET:!4903.50N/07201.75W_220/004g...t077h50b10132", harness.Transport.Written[1]);
    }

    [Fact]
    public async Task WeatherWithoutAPositionIsPositionlessAndTimestampedNow()
    {
        var harness = new CliHarness();

        var result = await harness.RunAsync(
            ["send", "weather", "--temp", "-7", "--rain-1h", "0.12", "--comment", "wRSW", "-c", Callsign, "-n"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"KD9XYZ-9>APZNET:_10111200c...s...g...t-07r012wRSW{Environment.NewLine}", result.Out);
    }

    [Theory]
    [InlineData(new[] { "--lat", "49", "--temp", "60" }, "--lat and --lon go together")]
    [InlineData(new string[0], "Give at least one reading")]
    [InlineData(new[] { "--snow", "2" }, "cannot carry snowfall")]
    [InlineData(new[] { "--lat", "49", "--lon", "-72", "--temp", "60", "--symbol", "/>" }, "weather station symbol")]
    public async Task WeatherRefusesWhatItCannotSend(string[] options, string error)
    {
        var harness = new CliHarness();

        var result = await harness.RunAsync(
            ["send", "weather", "-c", Callsign, "-n", .. options],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(error, result.Error, StringComparison.Ordinal);
        Assert.Equal(0, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task TelemetrySendsTheDefinitionsBeforeTheReport()
    {
        var harness = Verified();

        var result = await harness.RunAsync(
            [
                "send",
                "telemetry",
                "-c",
                Callsign,
                "--seq",
                "5",
                "--analog",
                "199,0,255,73,123",
                "--digital",
                "01101001",
                "--names",
                "Battery,Temp",
                "--equations",
                "0,5.2,0;0,0.53,-32",
            ],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                "KD9XYZ-9>APZNET::KD9XYZ-9 :PARM.Battery,Temp",
                "KD9XYZ-9>APZNET::KD9XYZ-9 :EQNS.0,5.2,0,0,0.53,-32",
                "KD9XYZ-9>APZNET:T#005,199,000,255,073,123,01101001",
            ],
            harness.Transport.Written.Skip(1)
        );
    }

    [Fact]
    public async Task TelemetryWithoutDefinitionsSendsOnlyTheReport()
    {
        var harness = new CliHarness();

        var result = await harness.RunAsync(
            ["send", "telemetry", "-c", Callsign, "-n", "--seq", "12", "--analog", "138,42"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"KD9XYZ-9>APZNET:T#012,138,042{Environment.NewLine}", result.Out);
    }

    [Theory]
    [InlineData(new[] { "--analog", "1" }, "--seq is required")]
    [InlineData(new[] { "--seq", "1" }, "--analog is required")]
    [InlineData(new[] { "--seq", "1", "--analog", "13.8" }, "whole numbers 0 to 999")]
    [InlineData(new[] { "--seq", "1", "--analog", "1,2,3,4,5", "--digital", "0101" }, "eight 0s and 1s")]
    [InlineData(new[] { "--seq", "1", "--analog", "1", "--equations", "1,2" }, "three numbers per channel")]
    [InlineData(new[] { "--seq", "1", "--analog", "1", "--project", "x" }, "--project goes with --bits")]
    public async Task TelemetryRefusesWhatItCannotSend(string[] options, string error)
    {
        var harness = new CliHarness();

        var result = await harness.RunAsync(
            ["send", "telemetry", "-c", Callsign, "-n", .. options],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(error, result.Error, StringComparison.Ordinal);
    }
}
