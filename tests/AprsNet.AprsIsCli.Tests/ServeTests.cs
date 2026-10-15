using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AprsNet.AprsIsCli.Tests;

public class ServeTests
{
    [Theory]
    [InlineData(new[] { "serve" }, "Option '--filter' is required.")]
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
                "3s",
            ],
            token
        );

        using var tcp = await ConnectAsync(port, token);
        using var reader = new StreamReader(tcp.GetStream(), Encoding.Latin1);
        await tcp.GetStream().WriteAsync("user N0CALL pass -1 vers test 1.0 filter p/N1\r\n"u8.ToArray(), token);
        Assert.StartsWith("# aprscli", await reader.ReadLineAsync(token));
        Assert.Equal("# logresp N0CALL unverified, server T2LOCAL", await reader.ReadLineAsync(token));
        Assert.Equal("# filter p/N1 active", await reader.ReadLineAsync(token));
        Assert.Equal("N1ABC>APRS,WIDE1-1,qAR,IGATE1:>first", await reader.ReadLineAsync(token));
        Assert.Equal("N1ABC>APRS,qAC,T2TEST:>second", await reader.ReadLineAsync(token));

        var result = await run;
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
