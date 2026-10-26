using System.Net;
using System.Net.Sockets;
using System.Text;
using AprsNet.AprsIsTcpClient;
using AprsNet.Ax25;
using AprsNet.Kiss;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli.Tests;

/// <summary><c>serve --igate</c>: a fake TNC hears packets, and the fake APRS-IS server gets what the IGate gates.</summary>
public class IGateServeTests
{
    private const string IGate = "N0CALL-10";

    // APRS101's example position, never a real station's.
    private static readonly string[] _place = ["--lat", "49.0583", "--lon", "-72.0292"];

    [Theory]
    [InlineData(new[] { "serve", "--tnc", "tnc", "--igate" }, "--igate needs --callsign")]
    [InlineData(new[] { "serve", "-f", "p/N", "-c", IGate, "--igate" }, "--igate needs a --tnc to gate from.")]
    [InlineData(new[] { "serve", "--tnc", "tnc", "-c", IGate, "--igate" }, "--igate needs --lat and --lon")]
    [InlineData(
        new[] { "serve", "--tnc", "tnc", "-c", IGate, "--igate", "--lat", "91", "--lon", "0" },
        "--lat must be -90 to 90"
    )]
    [InlineData(
        new[] { "serve", "--tnc", "tnc", "-c", IGate, "--igate", "--lat", "49", "--lon", "-72", "--symbol", "R" },
        "--symbol must be a table and a code"
    )]
    [InlineData(
        new[]
        {
            "serve",
            "--tnc",
            "tnc",
            "-c",
            IGate,
            "--igate",
            "--lat",
            "49",
            "--lon",
            "-72",
            "--beacon-interval",
            "30s",
        },
        "--beacon-interval '30s' is not valid."
    )]
    [InlineData(new[] { "serve", "--tnc", "tnc", "--lat", "49" }, "--lat only applies with --igate.")]
    [InlineData(new[] { "serve", "--tnc", "tnc", "--beacon-interval", "10m" }, "--beacon-interval only applies")]
    public async Task BadIGateOptionsAreUsageErrors(string[] args, string message)
    {
        var harness = new CliHarness();
        harness.Environment["APRS_PASSCODE"] = AprsIsPasscode
            .Compute(IGate)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        var result = await harness.RunAsync(args, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIGateNeedsAPasscode()
    {
        var result = await new CliHarness().RunAsync(
            ["serve", "--tnc", "tnc", "-c", IGate, "--igate", .. _place],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("--igate needs a verified login: set APRS_PASSCODE", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnverifiedLoginStopsTheIGate()
    {
        var token = TestContext.Current.CancellationToken;
        using var tnc = new TcpListener(IPAddress.Loopback, 0);
        tnc.Start();
        var harness = Harness(verified: false);

        var result = await harness.RunAsync(
            ["serve", "--tnc", Address(tnc), "--listen", $"127.0.0.1:{FreePort()}", "-c", IGate, "--igate", .. _place],
            token
        );

        Assert.Equal(4, result.ExitCode);
        Assert.Contains($"the server did not accept the passcode for {IGate}", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(
            AprsIsPasscode.Compute(IGate).ToString(System.Globalization.CultureInfo.InvariantCulture),
            result.Error,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task GatesWhatTheTncHearsAndBeaconsItsPosition()
    {
        var token = TestContext.Current.CancellationToken;
        using var tnc = new TcpListener(IPAddress.Loopback, 0);
        tnc.Start();
        var harness = Harness(verified: true);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var (port, webPort) = (FreePort(), FreePort());
        var run = harness.RunAsync(
            [
                "serve",
                "--tnc",
                Address(tnc),
                "--listen",
                $"127.0.0.1:{port}",
                "--http",
                $"127.0.0.1:{webPort}",
                "-c",
                IGate,
                "--igate",
                .. _place,
                "--comment",
                "test",
            ],
            stop.Token
        );

        using var radio = await tnc.AcceptTcpClientAsync(token);
        foreach (
            var line in new[]
            {
                "N1ABC>APRS,WIDE1-1,WIDE2-1:>heard direct",
                "N1ABC>APRS,DIGI1*,WIDE2-1:>heard direct", // the same packet, digipeated
                "N2ABC>APRS,NOGATE:>local only",
                "N3ABC>APRS:?APRS?",
                "N4ABC>APRS:}N5ABC>APRS,TCPIP,N4ABC*:>from the internet",
            }
        )
        {
            await radio.GetStream().WriteAsync(Kiss(line), token);
        }

        await radio.GetStream().WriteAsync(Kiss("N6ABC>APRS:>last"), token);
        await WaitForAsync(() => Written(harness).Contains("N6ABC>APRS,qAO,N0CALL-10:>last"), token);
        var health = await GetHealthAsync(webPort, token);

        await stop.CancelAsync();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(10), token);

        var sent = Written(harness).Skip(1).ToList(); // after the login line
        Assert.Equal(
            [
                "N0CALL-10>APZNET:!4903.50NR07201.75W&RX-only IGate test", // R& overlay: receive-only IGate
                "N1ABC>APRS,WIDE1-1,WIDE2-1,qAO,N0CALL-10:>heard direct",
                "N6ABC>APRS,qAO,N0CALL-10:>last",
            ],
            sent.Order(StringComparer.Ordinal).ToList()
        );
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "gating what the TNCs hear to rotate.aprs2.net:14580 as N0CALL-10 (receive-only, qAO)",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "gated 2 packets to APRS-IS (2 sent, 0 dropped), 1 beacons; refused from_internet 1, no_gate 1, "
                + "general_query 1, duplicate 1",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.Contains("\"igate.gated\":2", health, StringComparison.Ordinal);
        Assert.Contains("\"igate.refused.duplicate\":1", health, StringComparison.Ordinal);
        Assert.Contains("\"aprs-is\"", health, StringComparison.Ordinal);

        // Local clients still get everything the TNC heard, and nothing is sent upstream without a filter.
        Assert.Null(harness.ClientOptions!.Filter);
    }

    [Fact]
    public async Task ATwoWayIGateSendsMessagesForLocalStationsOnRf()
    {
        var token = TestContext.Current.CancellationToken;
        using var tnc = new TcpListener(IPAddress.Loopback, 0);
        tnc.Start();
        var harness = Harness(verified: true);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var (port, webPort) = (FreePort(), FreePort());
        var run = harness.RunAsync(
            [
                "serve",
                "--tnc",
                Address(tnc),
                "--listen",
                $"127.0.0.1:{port}",
                "--http",
                $"127.0.0.1:{webPort}",
                "-c",
                IGate,
                "--igate",
                .. _place,
                "--igate-to-rf",
            ],
            stop.Token
        );
        using var radio = await tnc.AcceptTcpClientAsync(token);
        await using var onAir = new KissStreamClient(radio.GetStream(), ownsStream: false);
        var transmitted = onAir.ReadPacketsAsync(token).GetAsyncEnumerator(token);

        // N1ABC is heard direct, so it is local; gated up with qAR, as a two-way IGate.
        await radio.GetStream().WriteAsync(Kiss("N1ABC>APRS,WIDE1-1:>on RF"), token);
        await WaitForAsync(() => Written(harness).Contains("N1ABC>APRS,WIDE1-1,qAR,N0CALL-10:>on RF"), token);

        harness.Transport.Server("W1AW>APRS,TCPIP*,qAC,T2TEST::N9NONE   :Not heard here{01");
        harness.Transport.Server("W1AW>APRS,TCPIP*,qAC,T2TEST::N1ABC    :Hello{01");
        harness.Transport.Server("W1AW>APRS,TCPIP*,qAC,T2TEST:!4903.50N/07201.75W-Writing");
        harness.Transport.Server("W1AW>APRS,TCPIP*,qAC,T2TEST:!4903.50N/07201.75W-Again");
        harness.Transport.Server("W2XYZ>APRS,TCPIP*,qAC,T2TEST:>The rest of the feed");

        Assert.True(await transmitted.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), token));
        Assert.Equal(
            "N0CALL-10>APZNET,WIDE1-1:}W1AW>APRS,TCPIP,N0CALL-10*::N1ABC    :Hello{01",
            transmitted.Current.ToString()
        );
        Assert.True(await transmitted.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), token));
        Assert.Equal(
            "N0CALL-10>APZNET,WIDE1-1:}W1AW>APRS,TCPIP,N0CALL-10*:!4903.50N/07201.75W-Writing",
            transmitted.Current.ToString()
        );
        var health = "";
        await WaitForAsync(
            () =>
                (health = GetHealthAsync(webPort, token).GetAwaiter().GetResult()).Contains(
                    "\"igate.rf.refused.not_for_a_station\":2",
                    StringComparison.Ordinal
                ),
            token
        );

        await stop.CancelAsync();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(10), token);

        Assert.Equal(0, result.ExitCode);
        Assert.False(radio.Available > 0, "the second position isn't sent");
        Assert.Contains("N0CALL-10>APZNET:!4903.50NI07201.75W&", Written(harness), StringComparer.Ordinal); // I& overlay: a two-way IGate, with no RX-only comment
        Assert.Contains("as N0CALL-10 (two-way, qAR)", result.Error, StringComparison.Ordinal);
        Assert.Contains(
            "gating messages from APRS-IS to stations heard on the TNC within 1 hops, sent via WIDE1-1",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "gated 2 messages and positions to RF (2 sent, 0 dropped); refused not_local 1",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.Contains("\"igate.rf.gated\":2", health, StringComparison.Ordinal);
        Assert.Contains("\"igate.rf.local_stations\":1", health, StringComparison.Ordinal);
    }

    /// <summary>
    /// Through a real Dire Wolf with no audio: a station's frame goes in from one KISS client, Dire Wolf passes it to the
    /// IGate (<c>KISSCOPY</c>), and the message the fake APRS-IS server sends for it comes back out to the station's
    /// client, followed by the sender's position once. Nothing reaches a radio. Opt-in, as the audio tests:
    /// <c>APRSNET_DIREWOLF_AUDIO=1</c> with <c>direwolf</c> on the PATH, as CI runs it.
    /// </summary>
    [Fact]
    public async Task ATwoWayIGateTransmitsThroughDireWolfWithNoAudio()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("APRSNET_DIREWOLF_AUDIO") != "1",
            "Set APRSNET_DIREWOLF_AUDIO=1 with direwolf on the PATH to gate through Dire Wolf."
        );
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("aprsnet-igate-");
        var kissPort = FreePort();
        var config = Path.Combine(directory.FullName, "direwolf.conf");
        await File.WriteAllLinesAsync(
            config,
            ["ADEVICE null null", "CHANNEL 0", "MYCALL N0DW", $"KISSPORT {kissPort}", "AGWPORT 0", "KISSCOPY 1"],
            token
        );
        using var direwolf = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("direwolf", $"-c {config} -t 0 -q hd")
            {
                WorkingDirectory = directory.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        )!;
        try
        {
            KissTcpClient station;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (true)
            {
                station = new KissTcpClient(
                    new KissTcpOptions
                    {
                        Host = "127.0.0.1",
                        Port = kissPort,
                        TransmitLimit = KissTransmitLimit.Off,
                    }
                );
                try
                {
                    await station.ConnectAsync(token);
                    break;
                }
                catch (SocketException) when (DateTime.UtcNow < deadline)
                {
                    await station.DisposeAsync();
                    await Task.Delay(100, token);
                }
            }

            await using var _ = station;
            var harness = Harness(verified: true);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            var run = harness.RunAsync(
                [
                    "serve",
                    "--tnc",
                    $"127.0.0.1:{kissPort}",
                    "--listen",
                    $"127.0.0.1:{FreePort()}",
                    "-c",
                    IGate,
                    "--igate",
                    .. _place,
                    "--igate-to-rf",
                ],
                stop.Token
            );
            var heard = station.ReadPacketsAsync(token).GetAsyncEnumerator(token);
            await Task.Delay(1000, token); // the IGate connected to Dire Wolf

            await station.SendAsync(AprsPacket.Parse("N1ABC>APRS,WIDE1-1:>on RF"), token);
            await WaitForAsync(() => Written(harness).Contains("N1ABC>APRS,WIDE1-1,qAR,N0CALL-10:>on RF"), token);
            harness.Transport.Server("W1AW>APRS,TCPIP*,qAC,T2TEST::N1ABC    :Through Dire Wolf{01");
            harness.Transport.Server("W1AW>APRS,TCPIP*,qAC,T2TEST:!4903.50N/07201.75W-Writing");

            Assert.True(await heard.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), token));
            Assert.Equal(
                "N0CALL-10>APZNET,WIDE1-1:}W1AW>APRS,TCPIP,N0CALL-10*::N1ABC    :Through Dire Wolf{01",
                heard.Current.ToString()
            );
            Assert.True(await heard.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), token));
            Assert.Equal(
                "N0CALL-10>APZNET,WIDE1-1:}W1AW>APRS,TCPIP,N0CALL-10*:!4903.50N/07201.75W-Writing",
                heard.Current.ToString()
            );

            await stop.CancelAsync();
            Assert.Equal(0, (await run.WaitAsync(TimeSpan.FromSeconds(10), token)).ExitCode);
        }
        finally
        {
            direwolf.Kill();
            await direwolf.WaitForExitAsync(CancellationToken.None);
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(new[] { "--tnc", "other" }, "--igate-to-rf needs exactly one --tnc")]
    [InlineData(new[] { "--rf-path", "WIDE1-1,WIDE2-1,WIDE3-3" }, "--rf-path 'WIDE1-1,WIDE2-1,WIDE3-3' is not valid")]
    public async Task BadTwoWayOptionsAreUsageErrors(string[] extra, string message)
    {
        var harness = Harness(verified: true);

        var result = await harness.RunAsync(
            ["serve", "--tnc", "tnc", "-c", IGate, "--igate", .. _place, "--igate-to-rf", .. extra],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoWayNeedsTheIGate()
    {
        var result = await new CliHarness().RunAsync(
            ["serve", "--tnc", "tnc", "--igate-to-rf"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("--igate-to-rf only applies with --igate.", result.Error, StringComparison.Ordinal);
    }

    private static CliHarness Harness(bool verified)
    {
        var harness = new CliHarness().LoginAccepted(IGate, verified);
        harness.Environment["APRS_PASSCODE"] = AprsIsPasscode
            .Compute(IGate)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        return harness;
    }

    private static List<string> Written(CliHarness harness)
    {
        lock (harness.Transport.Written)
        {
            return [.. harness.Transport.Written];
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(10, token);
        }
    }

    private static async Task<string> GetHealthAsync(int port, CancellationToken token)
    {
        using var http = new TcpClient();
        await http.ConnectAsync(IPAddress.Loopback, port, token);
        await http.GetStream().WriteAsync("GET /health HTTP/1.1\r\nHost: x\r\n\r\n"u8.ToArray(), token);
        return await new StreamReader(http.GetStream(), Encoding.UTF8).ReadToEndAsync(token);
    }

    private static string Address(TcpListener listener) => $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line))).Encode();

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
