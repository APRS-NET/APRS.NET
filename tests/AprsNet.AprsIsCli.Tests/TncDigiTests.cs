using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AprsNet.Ax25;
using AprsNet.Kiss;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli.Tests;

/// <summary><c>aprscli tnc digi</c> on a fake TNC over a local socket, and through a Dire Wolf with no audio.</summary>
public sealed class TncDigiTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly TcpListener _tnc = new(IPAddress.Loopback, 0);

    public TncDigiTests()
    {
        _tnc.Start();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string TncAddress => $"127.0.0.1:{((IPEndPoint)_tnc.LocalEndpoint).Port}";

    public void Dispose() => _tnc.Dispose();

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line))).Encode();

    [Fact]
    public async Task AFillInDigipeaterRepeatsWide1AndSaysWhyItRefusesTheRest()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = new CliHarness().RunAsync(
            ["tnc", "digi", "--tnc", TncAddress, "--call", "n0dig-1", "--transmit-limit", "off"],
            stop.Token
        );
        using var radio = await _tnc.AcceptTcpClientAsync(Token);
        await using var client = new KissStreamClient(radio.GetStream(), ownsStream: false);
        var heard = client.ReadFramesAsync(Token).GetAsyncEnumerator(Token);

        await radio.GetStream().WriteAsync(Kiss("N0CALL-9>APRS,WIDE2-2:>too wide for fill-in"), Token);
        await radio.GetStream().WriteAsync(Kiss("N0CALL-9>APRS,WIDE1-1,WIDE2-1:>fill me in"), Token);
        Assert.True(await heard.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        Assert.Equal("N0CALL-9>APRS,N0DIG-1*,WIDE2-1:>fill me in", heard.Current.ToTnc2());

        // Heard again from another digipeater: a copy, not repeated twice.
        await radio.GetStream().WriteAsync(Kiss("N0CALL-9>APRS,WIDE1-1:>fill me in"), Token);
        await Task.Delay(300, Token);
        await stop.CancelAsync();
        var result = await run.WaitAsync(Wait, Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("digipeating as N0DIG-1 on the TNC at", result.Error, StringComparison.Ordinal);
        Assert.Contains(
            "not repeated (not for this digipeater): N0CALL-9>APRS,WIDE2-2:>too wide",
            result.Out,
            StringComparison.Ordinal
        );
        Assert.Contains("repeated N0CALL-9>APRS,N0DIG-1*,WIDE2-1:>fill me in", result.Out, StringComparison.Ordinal);
        Assert.Contains(
            "not repeated (repeated already): N0CALL-9>APRS,WIDE1-1:",
            result.Out,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "heard 3 frames and repeated 1; not repeated: 1 not for this digipeater, 1 repeated already",
            result.Error,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task AWideAreaDigipeaterCountsDownAndTraps()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = new CliHarness().RunAsync(
            ["tnc", "digi", "--tnc", TncAddress, "-c", "N0DIG-1", "--wide", "--transmit-limit", "off", "-q"],
            stop.Token
        );
        using var radio = await _tnc.AcceptTcpClientAsync(Token);
        await using var client = new KissStreamClient(radio.GetStream(), ownsStream: false);
        var heard = client.ReadFramesAsync(Token).GetAsyncEnumerator(Token);

        await radio.GetStream().WriteAsync(Kiss("N0CALL-9>APRS,WIDE2-2:>two hops"), Token);
        Assert.True(await heard.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        Assert.Equal("N0CALL-9>APRS,N0DIG-1*,WIDE2-1:>two hops", heard.Current.ToTnc2());

        await radio.GetStream().WriteAsync(Kiss("N0CALL-9>APRS,WIDE7-7:>all the hops"), Token);
        Assert.True(await heard.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        Assert.Equal("N0CALL-9>APRS,N0DIG-1*:>all the hops", heard.Current.ToTnc2());

        await radio.GetStream().WriteAsync(Kiss("N1ABC>APRS:>no path"), Token);
        await Task.Delay(300, Token);
        await stop.CancelAsync();
        var result = await run.WaitAsync(Wait, Token);
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("not repeated", result.Out, StringComparison.Ordinal); // -q
        Assert.Contains("WIDEn-N up to 2 hops", result.Error, StringComparison.Ordinal);
        Assert.Contains("repeated 2; not repeated: 1 no hops left", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "--call", "N0DIG-16" }, "--call 'N0DIG-16' is not valid")]
    [InlineData(new[] { "--call", "N0DIG-1", "--max-hops", "8" }, "--max-hops must be 1 to 7")]
    [InlineData(new[] { "--call", "N0DIG-1", "--alias", "not ok" }, "--alias must be one to six letters")]
    [InlineData(new[] { "--call", "N0DIG-1", "--radio-port", "16" }, "--radio-port must be 0 to 15")]
    [InlineData(new[] { "--call", "N0DIG-1", "-d", "soon" }, "--duration 'soon' is not valid")]
    public async Task BadOptionsAreRefused(string[] options, string message)
    {
        var result = await new CliHarness().RunAsync(["tnc", "digi", "--tnc", "localhost", .. options], Token);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Through a real Dire Wolf with no audio: a station's frame goes in from one KISS client, Dire Wolf passes it to
    /// the digipeater (<c>KISSCOPY</c>), and the repeat the digipeater sends comes back out to the station's client.
    /// Nothing reaches a radio. Opt-in, as the audio tests: <c>APRSNET_DIREWOLF_AUDIO=1</c> with <c>direwolf</c> on the
    /// PATH, as CI runs it.
    /// </summary>
    [Fact]
    public async Task RepeatsThroughDireWolfWithNoAudio()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("APRSNET_DIREWOLF_AUDIO") != "1",
            "Set APRSNET_DIREWOLF_AUDIO=1 with direwolf on the PATH to digipeat through Dire Wolf."
        );
        var directory = Directory.CreateTempSubdirectory("aprsnet-digi-");
        var port = FreePort();
        var config = Path.Combine(directory.FullName, "direwolf.conf");
        await File.WriteAllLinesAsync(
            config,
            ["ADEVICE null null", "CHANNEL 0", "MYCALL N0DW", $"KISSPORT {port}", "AGWPORT 0", "KISSCOPY 1"],
            Token
        );
        using var direwolf = Process.Start(
            new ProcessStartInfo("direwolf", $"-c {config} -t 0 -q hd")
            {
                WorkingDirectory = directory.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        )!;
        try
        {
            await using var station = await ConnectAsync(port);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var run = new CliHarness().RunAsync(
                ["tnc", "digi", "--tnc", $"127.0.0.1:{port}", "--call", "N0DIG-1", "--transmit-limit", "off"],
                stop.Token
            );
            var heard = station.ReadPacketsAsync(Token).GetAsyncEnumerator(Token);
            await Task.Delay(1000, Token); // the digipeater connected to Dire Wolf

            await station.SendAsync(AprsPacket.Parse("N0CALL-9>APRS,WIDE1-1,WIDE2-1:>through Dire Wolf"), Token);
            Assert.True(await heard.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
            Assert.Equal("N0CALL-9>APRS,N0DIG-1*,WIDE2-1:>through Dire Wolf", heard.Current.ToString());

            await stop.CancelAsync();
            var result = await run.WaitAsync(Wait, Token);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("heard 1 frames and repeated 1", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            direwolf.Kill();
            await direwolf.WaitForExitAsync(CancellationToken.None);
            directory.Delete(recursive: true);
        }
    }

    private static async Task<KissTcpClient> ConnectAsync(int port)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (true)
        {
            var client = new KissTcpClient(
                new KissTcpOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    TransmitLimit = KissTransmitLimit.Off,
                }
            );
            try
            {
                await client.ConnectAsync(Token);
                return client;
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                await client.DisposeAsync();
                await Task.Delay(100, Token);
            }
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
