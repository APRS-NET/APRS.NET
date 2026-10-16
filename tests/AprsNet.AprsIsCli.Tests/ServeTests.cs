using System.Net;
using System.Net.Sockets;
using System.Text;
using AprsNet.Ax25;
using AprsNet.Kiss;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli.Tests;

public class ServeTests
{
    [Theory]
    [InlineData(new[] { "serve" }, "nothing to serve: give --filter for an APRS-IS feed, --tnc for a TNC, or both.")]
    [InlineData(new[] { "serve", "--tnc", "tnc:x" }, "--tnc 'tnc:x' is not valid.")]
    [InlineData(new[] { "serve", "--tnc", "tnc", "--tnc", "tnc:8001" }, "--tnc 'tnc:8001' is given twice.")]
    [InlineData(new[] { "serve", "-f", "r/91/0/10" }, "'91' is not a latitude.")]
    [InlineData(new[] { "serve", "-f", "  " }, "an upstream filter is required.")]
    [InlineData(new[] { "serve", "-f", "p/N0", "--listen", "localhost" }, "--listen 'localhost' is not valid.")]
    [InlineData(new[] { "serve", "-f", "p/N0", "--listen", "127.0.0.1" }, "--listen '127.0.0.1' is not valid.")]
    [InlineData(new[] { "serve", "-f", "p/N0", "--name", "TOO-LONG-NAME" }, "Server name must be 1 to 9")]
    [InlineData(new[] { "serve", "-f", "p/N0", "--history", "soon" }, "--history 'soon' is not valid.")]
    [InlineData(new[] { "serve", "-f", "p/N0", "--duration", "x" }, "--duration 'x' is not valid.")]
    [InlineData(new[] { "serve", "-f", "p/N0", "--server", "a:b:c" }, "--server 'a:b:c' is not valid.")]
    public async Task BadOptionsAreUsageErrors(string[] args, string message)
    {
        var result = await new CliHarness().RunAsync(args, TestContext.Current.CancellationToken);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServesTheFeedToLocalClientsWithTheirOwnFilters()
    {
        var token = TestContext.Current.CancellationToken;
        var harness = new CliHarness().LoginAccepted();
        harness.Transport.Server("N1ABC>APRS,WIDE1-1,qAR,IGATE1:>first");
        harness.Transport.Server("# aprsc keepalive");
        harness.Transport.Server("N1ABC>APRS,DIGI*,qAR,IGATE2:>first");
        harness.Transport.Server("N2ABC>APRS,qAC,T2TEST:>other");
        harness.Transport.Server("N1ABC>APRS,qAC,T2TEST:>second");

        // The test stops the run once it has read what it needs, as Ctrl+C would; a short --duration made the run end
        // before a slow runner had connected and read the packets.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var port = FreePort();
        var run = harness.RunAsync(
            [
                "serve",
                "-c",
                "NZ0P",
                "-f",
                "r/39/-95/500 p/N",
                "--listen",
                $"127.0.0.1:{port}",
                "--name",
                "T2LOCAL",
                "--history",
                "1h",
                "--duration",
                "1m",
            ],
            stop.Token
        );

        using var tcp = await ConnectAsync(port, token);
        using var reader = new StreamReader(tcp.GetStream(), Encoding.Latin1);
        await tcp.GetStream().WriteAsync("user N0CALL pass -1 vers test 1.0 filter p/N1\r\n"u8.ToArray(), token);
        Assert.StartsWith("# aprscli", await reader.ReadLineAsync(token));
        Assert.Equal("# logresp N0CALL unverified, server T2LOCAL", await reader.ReadLineAsync(token));
        Assert.Equal("# filter p/N1 active", await reader.ReadLineAsync(token));
        Assert.Equal("N1ABC>APRS,WIDE1-1,qAR,IGATE1:>first", await reader.ReadLineAsync(token));
        Assert.Equal("N1ABC>APRS,qAC,T2TEST:>second", await reader.ReadLineAsync(token));

        await stop.CancelAsync();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("r/39/-95/500 p/N", harness.ClientOptions!.Filter);
        Assert.Contains($"on 127.0.0.1:{port} as T2LOCAL", result.Error, StringComparison.Ordinal);
        Assert.Contains("served 3 packets (1 duplicates dropped)", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APortInUseIsAnError()
    {
        using var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        var port = ((IPEndPoint)taken.LocalEndpoint).Port;
        var result = await new CliHarness().RunAsync(
            ["serve", "-f", "p/N0", "--listen", $"127.0.0.1:{port}"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"cannot serve on 127.0.0.1:{port}", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServesWhatATncHearsWithoutAprsIs()
    {
        var token = TestContext.Current.CancellationToken;
        using var tnc = new TcpListener(IPAddress.Loopback, 0);
        tnc.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var port = FreePort();
        var harness = new CliHarness();
        var run = harness.RunAsync(
            ["serve", "--tnc", $"127.0.0.1:{((IPEndPoint)tnc.LocalEndpoint).Port}", "--listen", $"127.0.0.1:{port}"],
            stop.Token
        );

        using var radio = await tnc.AcceptTcpClientAsync(token);
        using var client = await ConnectAsync(port, token);
        using var reader = await LogInAsync(client, "p/N", token);
        await radio.GetStream().WriteAsync(Kiss("N1ABC>APRS,WIDE1-1:>heard on RF"), token);
        Assert.Equal("N1ABC>APRS,WIDE1-1:>heard on RF", await reader.ReadLineAsync(token));

        await stop.CancelAsync();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(harness.ClientOptions);
        Assert.Contains(
            $"serving the TNC at 127.0.0.1:{((IPEndPoint)tnc.LocalEndpoint).Port} on 127.0.0.1:{port}",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.Contains("served 1 packets (0 duplicates dropped)", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("by source", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServesWebSocketClientsJson()
    {
        var token = TestContext.Current.CancellationToken;
        using var tnc = new TcpListener(IPAddress.Loopback, 0);
        tnc.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var (port, webPort) = (FreePort(), FreePort());
        var run = new CliHarness().RunAsync(
            [
                "serve",
                "--tnc",
                $"127.0.0.1:{((IPEndPoint)tnc.LocalEndpoint).Port}",
                "--listen",
                $"127.0.0.1:{port}",
                "--http",
                $"127.0.0.1:{webPort}",
            ],
            stop.Token
        );

        using var radio = await tnc.AcceptTcpClientAsync(token);
        using var socket = new System.Net.WebSockets.ClientWebSocket();
        var url = new Uri($"ws://127.0.0.1:{webPort}/aprs?filter=p/N");
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await socket.ConnectAsync(url, token);
                break;
            }
            catch (System.Net.WebSockets.WebSocketException) when (attempt < 100)
            {
                await Task.Delay(50, token);
            }
        }

        var buffer = new byte[8192];
        var greeting = await socket.ReceiveAsync(buffer, token);
        Assert.Contains(
            "\"filter\":\"p/N\"",
            System.Text.Encoding.UTF8.GetString(buffer, 0, greeting.Count),
            StringComparison.Ordinal
        );
        await radio.GetStream().WriteAsync(Kiss("N1ABC>APRS,WIDE1-1:>heard on RF"), token);
        var packet = await socket.ReceiveAsync(buffer, token);
        Assert.Contains(
            "\"raw\":\"N1ABC>APRS,WIDE1-1:>heard on RF\"",
            System.Text.Encoding.UTF8.GetString(buffer, 0, packet.Count),
            StringComparison.Ordinal
        );

        using (var http = new TcpClient())
        {
            await http.ConnectAsync(IPAddress.Loopback, webPort, token);
            await http.GetStream().WriteAsync("GET /health HTTP/1.1\r\nHost: x\r\n\r\n"u8.ToArray(), token);
            var response = await new StreamReader(http.GetStream()).ReadToEndAsync(token);
            Assert.StartsWith("HTTP/1.1 200 ", response, StringComparison.Ordinal);
            Assert.Contains("\"status\":\"healthy\"", response, StringComparison.Ordinal);
            Assert.Contains("\"connected\":true", response, StringComparison.Ordinal);
        }

        await stop.CancelAsync();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            $"on http://127.0.0.1:{webPort} (WebSocket clients at /aprs",
            result.Error,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task AnUnusableHttpAddressIsRefused()
    {
        var result = await new CliHarness().RunAsync(
            ["serve", "-f", "p/N0", "--http", "localhost"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("--http 'localhost' is not valid.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APacketOnAprsIsAndTheTncIsServedOnce()
    {
        var token = TestContext.Current.CancellationToken;
        using var tnc = new TcpListener(IPAddress.Loopback, 0);
        tnc.Start();
        var tncAddress = $"127.0.0.1:{((IPEndPoint)tnc.LocalEndpoint).Port}";
        var harness = new CliHarness().LoginAccepted();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var port = FreePort();
        var run = harness.RunAsync(
            ["serve", "-f", "r/39/-95/500", "--tnc", tncAddress, "--listen", $"127.0.0.1:{port}"],
            stop.Token
        );

        using var radio = await tnc.AcceptTcpClientAsync(token);
        using var client = await ConnectAsync(port, token);
        using var reader = await LogInAsync(client, "p/N", token);
        harness.Transport.Server("N1ABC>APRS,WIDE1-1,qAR,IGATE:>hello");
        Assert.Equal("N1ABC>APRS,WIDE1-1,qAR,IGATE:>hello", await reader.ReadLineAsync(token));
        await radio.GetStream().WriteAsync(Kiss("N1ABC>APRS,WIDE1-1:>hello"), token);
        await radio.GetStream().WriteAsync(Kiss("N2ABC>APRS:>only on RF"), token);
        Assert.Equal("N2ABC>APRS:>only on RF", await reader.ReadLineAsync(token));

        await stop.CancelAsync();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            $"(r/39/-95/500) and the TNC at {tncAddress} on 127.0.0.1:{port}",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.Contains("served 2 packets (1 duplicates dropped)", result.Error, StringComparison.Ordinal);
        Assert.Contains(
            $"by source: aprs-is 1 (0 duplicates), {tncAddress} 2 (1 duplicates)",
            result.Error,
            StringComparison.Ordinal
        );
    }

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line))).Encode();

    private static async Task<StreamReader> LogInAsync(TcpClient client, string filter, CancellationToken token)
    {
        var reader = new StreamReader(client.GetStream(), Encoding.Latin1);
        await client
            .GetStream()
            .WriteAsync(Encoding.Latin1.GetBytes($"user N0CALL pass -1 vers test 1.0 filter {filter}\r\n"), token);
        Assert.StartsWith("# aprscli", await reader.ReadLineAsync(token));
        Assert.StartsWith("# logresp N0CALL unverified", await reader.ReadLineAsync(token));
        Assert.Equal($"# filter {filter} active", await reader.ReadLineAsync(token));
        return reader;
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static async Task<TcpClient> ConnectAsync(int port, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var tcp = new TcpClient();
            try
            {
                await tcp.ConnectAsync(IPAddress.Loopback, port, token);
                return tcp;
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                tcp.Dispose();
                await Task.Delay(20, token);
            }
        }
    }
}
