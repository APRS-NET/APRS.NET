using AprsNet.AprsIsTcpClient;
using AprsNet.AprsIsTcpClient.Tests;

namespace AprsNet.AprsIsCli.Tests;

public class SendTests
{
    private const string Callsign = "KD9XYZ-9";

    private static CliHarness Verified(string status = "verified")
    {
        var harness = new CliHarness();
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
}
