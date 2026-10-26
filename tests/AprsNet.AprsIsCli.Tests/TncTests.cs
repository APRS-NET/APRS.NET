using System.Net;
using System.Net.Sockets;
using AprsNet.Ax25;
using AprsNet.Kiss;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli.Tests;

public sealed class TncTests : IDisposable
{
    private readonly TcpListener _tnc = new(IPAddress.Loopback, 0);

    public TncTests()
    {
        _tnc.Start();
    }

    private string Address => $"127.0.0.1:{((IPEndPoint)_tnc.LocalEndpoint).Port}";

    public void Dispose() => _tnc.Dispose();

    private async Task Hear(params string[] lines)
    {
        using var tnc = await _tnc.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        foreach (var line in lines)
        {
            await tnc.GetStream()
                .WriteAsync(
                    KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line))).Encode(),
                    TestContext.Current.CancellationToken
                );
        }

        tnc.Client.Shutdown(SocketShutdown.Send);
    }

    [Fact]
    public async Task ListenPrintsThePacketsTheTncHears()
    {
        var run = new CliHarness().RunAsync(
            ["tnc", "listen", "--tnc", Address, "-n", "2"],
            TestContext.Current.CancellationToken
        );
        await Hear("N0CALL-9>APRS,WIDE1-1:>One", "N0CALL-9>APRS:>Two");
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            $"N0CALL-9>APRS,WIDE1-1:>One{Environment.NewLine}N0CALL-9>APRS:>Two{Environment.NewLine}",
            result.Out
        );
    }

    [Theory]
    [InlineData(false, "raw")]
    [InlineData(true, "raw")]
    [InlineData(false, "json")]
    public async Task ListenWithAllPortsShowsThePortOnceMoreThanOneIsHeard(bool reconnect, string format)
    {
        string[] args = ["tnc", "listen", "--tnc", Address, "--all-ports", "--format", format, "-n", "3"];
        var run = new CliHarness().RunAsync(
            reconnect ? [.. args, "--reconnect"] : args,
            TestContext.Current.CancellationToken
        );
        using (var tnc = await _tnc.AcceptTcpClientAsync(TestContext.Current.CancellationToken))
        {
            foreach (
                var (line, port) in new[]
                {
                    ("N1ABC>APRS:>VHF", 0),
                    ("N2ABC>APRS:>HF", 1),
                    ("N1ABC>APRS:>VHF again", 0),
                }
            )
            {
                await tnc.GetStream()
                    .WriteAsync(
                        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)), port).Encode(),
                        TestContext.Current.CancellationToken
                    );
            }

            var result = await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(0, result.ExitCode);
            var lines = result.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            if (format == "raw")
            {
                Assert.Equal(["N1ABC>APRS:>VHF", "[port 1] N2ABC>APRS:>HF", "[port 0] N1ABC>APRS:>VHF again"], lines);
            }
            else
            {
                Assert.DoesNotContain("\"port\"", lines[0], StringComparison.Ordinal);
                Assert.StartsWith("{\"port\":1,", lines[1], StringComparison.Ordinal);
                Assert.StartsWith("{\"port\":0,", lines[2], StringComparison.Ordinal);
                System.Text.Json.JsonDocument.Parse(lines[1]).Dispose();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListenWarnsWhenTheTncSendsTextInsteadOfKiss(bool reconnect)
    {
        string[] args = ["tnc", "listen", "--tnc", Address, "-n", "1"];
        var run = new CliHarness().RunAsync(
            reconnect ? [.. args, "--reconnect"] : args,
            TestContext.Current.CancellationToken
        );
        using (var tnc = await _tnc.AcceptTcpClientAsync(TestContext.Current.CancellationToken))
        {
            // A TNC not in KISS mode prints its monitor lines; then it is switched to KISS and a frame follows.
            var text = string.Concat(
                Enumerable
                    .Range(1, 3)
                    .Select(i => $"N0CALL-{i}>APRS,WIDE1-1:>A packet heard while the TNC was not in KISS mode\r\n")
            );
            await tnc.GetStream()
                .WriteAsync(System.Text.Encoding.ASCII.GetBytes(text), TestContext.Current.CancellationToken);
            await tnc.GetStream()
                .WriteAsync(
                    KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL>APRS:>KISS"))).Encode(),
                    TestContext.Current.CancellationToken
                );
            var result = await run;

            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"N0CALL>APRS:>KISS{Environment.NewLine}", result.Out);
            Assert.Contains(
                $"aprscli: warning: the TNC at {Address}: The TNC is sending text, not KISS frames; is it in KISS mode?",
                result.Error,
                StringComparison.Ordinal
            );
            Assert.Contains("The TNC is sending KISS frames again.", result.Error, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("10m", false, "--quiet-warning needs --reconnect.")]
    [InlineData("soon", true, "--quiet-warning 'soon' is not valid.")]
    public async Task AQuietWarningMustBeADurationWithReconnect(string quiet, bool reconnect, string message)
    {
        string[] args = ["tnc", "listen", "--tnc", Address, "--quiet-warning", quiet];
        var result = await new CliHarness().RunAsync(
            reconnect ? [.. args, "--reconnect"] : args,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListenDecodesInTheChosenFormat()
    {
        var run = new CliHarness().RunAsync(
            ["tnc", "listen", "--tnc", Address, "-n", "1", "--format", "decoded"],
            TestContext.Current.CancellationToken
        );
        await Hear("N0CALL-9>APRS:!4903.50N/07201.75W>Mobile");
        var result = await run;

        Assert.Contains("position 49.05833 -72.02917", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATncThatClosesIsAConnectionFailure()
    {
        var run = new CliHarness().RunAsync(["tnc", "listen", "--tnc", Address], TestContext.Current.CancellationToken);
        await Hear("N0CALL>APRS:>Only one");
        var result = await run;

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("closed the connection", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithReconnectListeningCarriesOnAfterTheTncCloses()
    {
        var run = new CliHarness().RunAsync(
            ["tnc", "listen", "--tnc", Address, "--reconnect", "-n", "2"],
            TestContext.Current.CancellationToken
        );
        await Hear("N0CALL>APRS:>First connection");
        await Hear("N0CALL>APRS:>Second connection");
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            $"N0CALL>APRS:>First connection{Environment.NewLine}N0CALL>APRS:>Second connection{Environment.NewLine}",
            result.Out
        );
        Assert.Contains("closed the connection; reconnecting in 0s.", result.Error, StringComparison.Ordinal);
        Assert.Contains($"reconnected to the TNC at {Address}.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithReconnectATncThatIsNotUpYetIsWaitedFor()
    {
        var port = ((IPEndPoint)_tnc.LocalEndpoint).Port;
        var address = Address;
        _tnc.Stop();
        var run = new CliHarness().RunAsync(
            ["tnc", "listen", "--tnc", address, "--reconnect", "-n", "1"],
            TestContext.Current.CancellationToken
        );
        await Task.Delay(100, TestContext.Current.CancellationToken);
        using var late = new TcpListener(IPAddress.Loopback, port);
        late.Start();
        using (
            var tnc = await late.AcceptTcpClientAsync(TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)
        )
        {
            await tnc.GetStream()
                .WriteAsync(
                    KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL>APRS:>At last"))).Encode(),
                    TestContext.Current.CancellationToken
                );
            var result = await run;
            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"N0CALL>APRS:>At last{Environment.NewLine}", result.Out);
            // Whether an attempt is refused before the TNC comes up depends on the operating system: Windows retries a
            // refused loopback connection for about two seconds, so its first attempt may simply succeed late.
        }
    }

    [Theory]
    [InlineData(":8001", "is not valid")]
    [InlineData("tnc:99999", "is not valid")]
    [InlineData("tnc:port", "is not valid")]
    public async Task RefusesABadTncAddress(string tnc, string error)
    {
        var result = await new CliHarness().RunAsync(
            ["tnc", "listen", "--tnc", tnc],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(error, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesARadioPortAbove15()
    {
        var result = await new CliHarness().RunAsync(
            ["tnc", "listen", "--tnc", Address, "--radio-port", "16"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
    }
}
