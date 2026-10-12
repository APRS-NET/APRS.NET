using System.Text;
using AprsNet.AprsIsTcpClient.Tests;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli.Tests;

public class ListenTests
{
    private static readonly string[] _packets =
    [
        "W1AW>APRS,TCPIP*:>one",
        "K1ABC>APRS,TCPIP*:>two",
        "N2XYZ>APRS,TCPIP*:>three",
    ];

    private static CliHarness WithPackets(params string[] lines)
    {
        var harness = new CliHarness().LoginAccepted();
        foreach (var line in lines)
            harness.Transport.Server(line);

        return harness;
    }

    [Fact]
    public async Task PrintsRawLinesAndStopsAtCount()
    {
        var harness = WithPackets(_packets);

        var result = await harness.RunAsync(
            ["listen", "--callsign", "NZ0P", "-n", "2"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"{_packets[0]}{Environment.NewLine}{_packets[1]}{Environment.NewLine}", result.Out);
    }

    [Fact]
    public async Task SendsTheCallsignFilterAndSoftwareNameInTheLogin()
    {
        var harness = WithPackets(_packets);

        await harness.RunAsync(
            ["listen", "-c", "nz0p", "-f", "r/35/-97/200", "-n", "1"],
            TestContext.Current.CancellationToken
        );

        var login = harness.Transport.Written[0];
        Assert.StartsWith("user NZ0P pass -1 vers aprscli ", login, StringComparison.Ordinal);
        Assert.EndsWith(" filter r/35/-97/200", login, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimestampsFlagPrefixesConsoleLines()
    {
        var harness = WithPackets(_packets);

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "-t", "-n", "1"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal($"2026-10-11T12:00:00.123Z {_packets[0]}{Environment.NewLine}", result.Out);
    }

    [Fact]
    public async Task CaptureAppendsTimestampedLinesEvenWithoutTheTimestampsFlag()
    {
        var harness = WithPackets(_packets);
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "nested", "cap.log");

        var first = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--capture", file, "-n", "2"],
            TestContext.Current.CancellationToken
        );
        var second = await new Func<Task<CliResult>>(async () =>
        {
            var again = WithPackets(_packets[2]);
            return await again.RunAsync(["listen", "-c", "NZ0P", "--capture", file, "-n", "1"]);
        })();

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(
            [
                $"2026-10-11T12:00:00.123Z {_packets[0]}",
                $"2026-10-11T12:00:00.123Z {_packets[1]}",
                $"2026-10-11T12:00:00.123Z {_packets[2]}",
            ],
            await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken)
        );
        Assert.DoesNotContain("2026-", first.Out, StringComparison.Ordinal); // console still untimestamped
    }

    [Fact]
    public async Task QuietSilencesTheConsoleButNotTheCapture()
    {
        var harness = WithPackets(_packets);
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "cap.log");

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "-q", "--capture", file, "-n", "3"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Out);
        Assert.Equal(3, (await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken)).Length);
    }

    [Fact]
    public async Task ConsoleEscapesControlCharactersButTheCaptureKeepsEveryByte()
    {
        var raw = "A>B:>\u001b[31mred\u0000\u001cé";
        var harness = WithPackets(raw);
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "cap.log");

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--capture", file, "-n", "1"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal("A>B:>\\x1B[31mred\\x00\\x1Cé" + Environment.NewLine, result.Out);
        var bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        var expected = Encoding.Latin1.GetBytes("2026-10-11T12:00:00.123Z " + raw + "\n");
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public async Task ServerCommentsAreNotPrintedOrCounted()
    {
        var harness = WithPackets("# keepalive", _packets[0], "# another", _packets[1]);

        var result = await harness.RunAsync(["listen", "-c", "NZ0P", "-n", "2"], TestContext.Current.CancellationToken);

        Assert.Equal($"{_packets[0]}{Environment.NewLine}{_packets[1]}{Environment.NewLine}", result.Out);
    }

    [Fact]
    public async Task DurationStopsAQuietFeedCleanly()
    {
        var harness = new CliHarness().LoginAccepted();

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "-d", "0.2s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task CtrlCStopsCleanly()
    {
        var harness = new CliHarness().LoginAccepted();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], cts.Token);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ServerClosingTheConnectionIsAConnectionFailure()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Close();

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("closed the connection", result.Error, StringComparison.Ordinal);
        Assert.Equal($"{_packets[0]}{Environment.NewLine}", result.Out);
    }

    [Fact]
    public async Task ConnectionErrorMidStreamIsAConnectionFailure()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Fail(new IOException("reset"));

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("connection lost", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASilentServerIsAConnectionFailureWithoutReconnect()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Fail(new TimeoutException("No data from the server for 90s."));

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("connection lost: No data from the server for 90s.", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task ReconnectResumesAfterTheServerClosesAndCountSpansConnections()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Close();
        harness.AddReconnect(_packets[1], _packets[2]);

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "-n", "2"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"{_packets[0]}{Environment.NewLine}{_packets[1]}{Environment.NewLine}", result.Out);
        Assert.Contains("closed the connection", result.Error, StringComparison.Ordinal);
        Assert.Contains("reconnecting to rotate.aprs2.net:14580 in 0s", result.Error, StringComparison.Ordinal);
        Assert.Equal(2, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task ReconnectResumesAfterTheServerGoesSilent()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Fail(new TimeoutException("No data from the server for 90s."));
        harness.AddReconnect(_packets[1]);

        using var capture = new TempDirectory();
        var file = Path.Combine(capture.Path, "c.log");
        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "-n", "2", "--capture", file],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No data from the server", result.Error, StringComparison.Ordinal);
        var captured = await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken);
        Assert.Equal(2, captured.Length);
        Assert.EndsWith(_packets[1], captured[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconnectKeepsRetryingAnUnreachableServerUntilTheDurationEnds()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Close();

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "-d", "1s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.True(harness.ConnectionAttempts >= 3, $"only {harness.ConnectionAttempts} attempts");
        Assert.Contains("could not connect", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconnectDoesNotRetryAFailedFirstConnection()
    {
        var harness = new CliHarness { ConnectFailure = SocketFailures.Refused() };

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "-d", "1s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(1, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task ReconnectStopsOnARejectedLogin()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Close();
        var second = new FakeAprsTransport();
        second.Server("# Login by user not allowed");
        harness.Reconnects.Enqueue(second);

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "-d", "5s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(4, result.ExitCode);
        Assert.Equal(2, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task MaxSilenceReconnectsWhenOnlyKeepalivesArrive()
    {
        var harness = WithPackets(_packets[0]);
        harness.AddReconnect(_packets[1]);

        var run = harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "--max-silence", "2s", "-n", "2"],
            TestContext.Current.CancellationToken
        );
        await Task.Delay(150, TestContext.Current.CancellationToken);
        harness.Transport.Server("# aprsc keepalive");
        harness.Transport.Server("# aprsc keepalive");
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "no packets from T2TEST for 2s (server keepalives in that time: 2)",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.Equal($"{_packets[0]}{Environment.NewLine}{_packets[1]}{Environment.NewLine}", result.Out);
        Assert.Equal(2, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task MaxSilenceWithoutReconnectIsAConnectionFailure()
    {
        var harness = new CliHarness().LoginAccepted();

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--max-silence", "0.2s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("no packets from T2TEST for 0.2s", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaxSilenceOffDisablesTheReconnectDefault()
    {
        var harness = new CliHarness().LoginAccepted();

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "--max-silence", "off", "-d", "0.3s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task AnInvalidMaxSilenceIsAUsageError()
    {
        var harness = new CliHarness().LoginAccepted();

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--max-silence", "soon"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("--max-silence 'soon' is not valid", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task UnreachableServerIsAConnectionFailure()
    {
        var harness = new CliHarness { ConnectFailure = SocketFailures.Refused() };

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--server", "127.0.0.1:9"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("could not connect to 127.0.0.1:9", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedDefaultCallsignExplainsAndExits4()
    {
        var harness = new CliHarness();
        harness.Transport.Server("# aprsc 2.1.21-gbfc2090");
        harness.Transport.Server("# Login by user not allowed");

        var result = await harness.RunAsync(["listen"], TestContext.Current.CancellationToken);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains("Login by user not allowed", result.Error, StringComparison.Ordinal);
        Assert.Contains("placeholder callsign N0CALL", result.Error, StringComparison.Ordinal);
        Assert.Contains("--callsign", result.Error, StringComparison.Ordinal);
        Assert.Equal("", result.Out);
    }

    [Fact]
    public async Task RejectedCustomCallsignGetsNoPlaceholderHint()
    {
        var harness = new CliHarness();
        harness.Transport.Server("# Login by user not allowed");

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], TestContext.Current.CancellationToken);

        Assert.Equal(4, result.ExitCode);
        Assert.DoesNotContain("placeholder", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginClosedBeforeAnyAnswerExits4()
    {
        var harness = new CliHarness();
        harness.Transport.Close();

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], TestContext.Current.CancellationToken);

        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public async Task DefaultsAreN0callReadOnlyOnTheWorldwideRotate()
    {
        var harness = new CliHarness().LoginAccepted("N0CALL");

        await harness.RunAsync(["listen", "-d", "0.1s"], TestContext.Current.CancellationToken);

        Assert.Equal("N0CALL", harness.ClientOptions!.Callsign);
        Assert.Equal(-1, harness.ClientOptions.Passcode);
        Assert.Equal("rotate.aprs2.net:14580", harness.ClientOptions.Endpoint.ToString());
    }

    [Theory]
    [InlineData("--region noam", "noam.aprs2.net:14580")]
    [InlineData("--region euro --full-feed", "euro.aprs2.net:10152")]
    [InlineData("--region asia --port 20152", "asia.aprs2.net:20152")]
    [InlineData("--server example.org", "example.org:14580")]
    [InlineData("--server example.org:1234", "example.org:1234")]
    [InlineData("--server example.org --full-feed", "example.org:10152")]
    [InlineData("--server example.org --port 99", "example.org:99")]
    [InlineData("--full-feed", "rotate.aprs2.net:10152")]
    public async Task ResolvesTheEndpoint(string args, string expected)
    {
        var harness = new CliHarness().LoginAccepted();

        await harness.RunAsync(
            ["listen", "-c", "NZ0P", "-d", "0.1s", .. args.Split(' ')],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expected, harness.ClientOptions!.Endpoint.ToString());
    }

    [Fact]
    public async Task PasscodeComesFromTheEnvironmentAndTheOptionWins()
    {
        var fromEnvironment = new CliHarness().LoginAccepted();
        fromEnvironment.Environment["APRS_PASSCODE"] = "12345";
        var envResult = await fromEnvironment.RunAsync(
            ["listen", "-c", "NZ0P", "-d", "0.1s"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(12345, fromEnvironment.ClientOptions!.Passcode);

        var optionWins = new CliHarness().LoginAccepted();
        optionWins.Environment["APRS_PASSCODE"] = "12345";
        var optionResult = await optionWins.RunAsync(
            ["listen", "-c", "NZ0P", "--passcode", "777", "-d", "0.1s"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(777, optionWins.ClientOptions!.Passcode);

        foreach (var text in new[] { envResult.Out, envResult.Error, optionResult.Out, optionResult.Error })
        {
            Assert.DoesNotContain("12345", text, StringComparison.Ordinal);
            Assert.DoesNotContain("777", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("listen --server a.example --region noam", "cannot be used together")]
    [InlineData("listen --port 14580 --full-feed", "--port and --full-feed")]
    [InlineData("listen --server a.example:1234 --port 99", "already includes a port")]
    [InlineData("listen --server a.example:1234 --full-feed", "already includes a port")]
    [InlineData("listen --port 0", "between 1 and 65535")]
    [InlineData("listen --port 70000", "between 1 and 65535")]
    [InlineData("listen --server a:b:c", "not valid")]
    [InlineData("listen --duration soon", "not valid")]
    [InlineData("listen --duration 0s", "not valid")]
    [InlineData("listen --duration -5m", "not valid")]
    [InlineData("listen --count 0", "at least 1")]
    [InlineData("listen --quiet", "silently")]
    [InlineData("listen --capture", "")]
    [InlineData("listen --callsign TOOLONGCALL1", "Callsign")]
    [InlineData("listen --callsign N0CALL --filter \"a\\nb\"", "")]
    [InlineData("listen --region mars", "")]
    [InlineData("listen --format json", "")]
    [InlineData("listen --bogus", "")]
    [InlineData("nope", "")]
    [InlineData("", "")]
    public async Task UsageErrorsExit3WithoutConnecting(string commandLine, string expectedText)
    {
        var harness = new CliHarness().LoginAccepted();
        var args =
            commandLine.Length == 0
                ? []
                : commandLine
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Replace("\\n", "\n", StringComparison.Ordinal))
                    .ToArray();

        var result = await harness.RunAsync(args, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(expectedText, result.Error, StringComparison.Ordinal);
        Assert.Null(harness.ClientOptions);
        Assert.False(harness.Transport.Connected);
    }

    [Fact]
    public async Task ANonNumericPasscodeVariableIsAUsageErrorAndIsNotEchoed()
    {
        var harness = new CliHarness().LoginAccepted();
        harness.Environment["APRS_PASSCODE"] = "hunter2";

        var result = await harness.RunAsync(["listen"], TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("APRS_PASSCODE must be an integer", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PacketsAreNotLoggedByDefaultEvenWithAnEndpoint()
    {
        var harness = WithPackets(_packets);
        harness.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318";

        var result = await harness.RunAsync(["listen", "-c", "NZ0P", "-n", "3"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(harness.Logs.Entries, e => e.Category == "aprscli.packets");
    }

    [Fact]
    public async Task LogPacketsLogsEachPacketToThePacketsCategory()
    {
        var harness = WithPackets("W1AW>APRS,TCPIP*:>one", "# comment", "K1ABC>APRS:>\u001b[31mtwo");
        harness.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318";

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--log-packets", "-n", "2"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        var logged = harness.Logs.Entries.Where(e => e.Category == "aprscli.packets").ToList();
        Assert.Equal(
            [
                "Packet from W1AW: W1AW>APRS,TCPIP*:>one",
                "Packet from K1ABC: K1ABC>APRS:>\\x1B[31mtwo", // control characters escaped for the backend
            ],
            logged.Select(e => e.Message)
        );
        Assert.All(logged, e => Assert.Equal(LogLevel.Information, e.Level));
        Assert.Contains("W1AW", result.Out, StringComparison.Ordinal); // console output is unaffected
    }

    [Fact]
    public async Task LogPacketsWithoutAnOtlpEndpointIsAUsageError()
    {
        var harness = WithPackets(_packets);

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--log-packets"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", result.Error, StringComparison.Ordinal);
        Assert.False(harness.Transport.Connected);
    }

    [Fact]
    public async Task HelpSucceeds()
    {
        var result = await new CliHarness().RunAsync(["listen", "--help"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--timestamps", result.Out, StringComparison.Ordinal);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aprscli-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
