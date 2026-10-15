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
