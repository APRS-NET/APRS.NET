using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>Runs the real client and transport against a tiny APRS-IS server on loopback.</summary>
public class TcpAprsTransportTests
{
    [Fact]
    public async Task ConnectsLogsInAndReceivesPacketsOverARealSocket()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var loginLine = new TaskCompletionSource<string>();
        var server = Task.Run(
            async () =>
            {
                {
                    // ReSharper disable once AccessToDisposedClosure
                    using var socket = await listener.AcceptTcpClientAsync();
                    var stream = socket.GetStream();
                    await Send(stream, "# aprsc 2.1.19-test\r\n");

                    var login = await ReadLine(stream);
                    loginLine.SetResult(login);
                    await Send(stream, "# logresp NZ0P unverified, server T2LOOP\r\n");
                    await Send(stream, "W1AW>APRS,TCPIP*:>sp");
                    await Task.Delay(50); // split mid-line
                    await Send(stream, "lit\r\n# keepalive\r\nK1ABC>APRS:>second\r\n");
                }
            },
            TestContext.Current.CancellationToken
        );

        var options = new AprsIsClientOptions
        {
            Endpoint = new AprsIsEndpoint("127.0.0.1", port),
            Callsign = "NZ0P",
            SoftwareName = "test",
            SoftwareVersion = "1.0",
            Filter = "t/m",
        };
        await using var client = new AprsIsClient(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await client.ConnectAsync(cts.Token);
        var lines = new List<string>();
        await foreach (var line in client.ReadLinesAsync(cts.Token))
        {
            lines.Add(line);
            if (lines.Count == 2)
                break;
        }

        await server;
        Assert.Equal("user NZ0P pass -1 vers test 1.0 filter t/m", await loginLine.Task);
        Assert.Equal(new LoginResult("NZ0P", LoginStatus.Unverified, "T2LOOP"), result);
        Assert.Equal(["W1AW>APRS,TCPIP*:>split", "K1ABC>APRS:>second"], lines);
    }

    [Fact]
    public async Task N0CallIsRejectedOverARealSocketEvenThoughTheServerStaysOpen()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var release = new TaskCompletionSource();

        var server = Task.Run(
            async () =>
            {
                {
                    // ReSharper disable once AccessToDisposedClosure
                    using var socket = await listener.AcceptTcpClientAsync();
                    var stream = socket.GetStream();
                    await Send(stream, "# aprsc 2.1.21-gbfc2090\r\n");
                    Assert.Equal("user N0CALL pass -1 vers test 1.0", await ReadLine(stream));
                    await Send(stream, "# Login by user not allowed\r\n");
                }

                await release.Task; // like the real servers, do not hang up
            },
            TestContext.Current.CancellationToken
        );

        var options = new AprsIsClientOptions
        {
            Endpoint = new AprsIsEndpoint("127.0.0.1", port),
            Callsign = "N0CALL",
            SoftwareName = "test",
            SoftwareVersion = "1.0",
        };
        await using var client = new AprsIsClient(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var ex = await Assert.ThrowsAsync<LoginRejectedException>(() => client.ConnectAsync(cts.Token));

        Assert.Equal("Login by user not allowed", ex.ServerMessage);
        release.SetResult();
        await server;
    }

    [Fact]
    public async Task ConnectToAClosedPortFails()
    {
        // Hold the port bound but not listening for the whole test: connections to it are refused, and no parallel
        // test can claim it (freeing the port first raced with other tests and occasionally connected).
        using var closed = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        closed.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)closed.LocalEndPoint!).Port;

        var options = new AprsIsClientOptions { Endpoint = new AprsIsEndpoint("127.0.0.1", port), Callsign = "NZ0P" };
        await using var client = new AprsIsClient(options);

        await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync(TestContext.Current.CancellationToken));
    }

    private static Task Send(NetworkStream stream, string text)
    {
        return stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();
    }

    private static async Task<string> ReadLine(NetworkStream stream)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (await stream.ReadAsync(one) == 1)
        {
            if (one[0] == '\n')
                break;

            sb.Append((char)one[0]);
        }

        return sb.ToString().TrimEnd('\r');
    }
}
