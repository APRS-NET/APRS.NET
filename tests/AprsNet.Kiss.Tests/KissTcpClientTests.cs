using System.Net;
using System.Net.Sockets;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss.Tests;

/// <summary>Runs the client against a loopback TCP listener standing in for a TNC.</summary>
public sealed class KissTcpClientTests : IAsyncDisposable
{
    private readonly TcpListener _tnc = new(IPAddress.Loopback, 0);

    public KissTcpClientTests()
    {
        _tnc.Start();
    }

    private int Port => ((IPEndPoint)_tnc.LocalEndpoint).Port;

    public ValueTask DisposeAsync()
    {
        _tnc.Dispose();
        return ValueTask.CompletedTask;
    }

    private static byte[] Wire(string line, int port = 0) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)), port).Encode();

    [Fact]
    public async Task ReadsThePacketsTheTncHears()
    {
        await using var client = new KissTcpClient("127.0.0.1", Port);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        using var tnc = await _tnc.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        var stream = tnc.GetStream();
        byte[] heard = [.. Wire("N0CALL-9>APRS,WIDE1-1:>One"), .. Wire("N0CALL-9>APRS:>Two")];
        // Split mid-frame, as a serial bridge might deliver it.
        await stream.WriteAsync(heard.AsMemory(0, 9), TestContext.Current.CancellationToken);
        await stream.FlushAsync(TestContext.Current.CancellationToken);
        await stream.WriteAsync(heard.AsMemory(9), TestContext.Current.CancellationToken);
        tnc.Client.Shutdown(SocketShutdown.Send);

        var packets = new List<AprsPacket>();
        await foreach (var packet in client.ReadPacketsAsync(TestContext.Current.CancellationToken))
        {
            packets.Add(packet);
        }

        Assert.Equal(["N0CALL-9>APRS,WIDE1-1:>One", "N0CALL-9>APRS:>Two"], packets.Select(p => p.ToString()));
    }

    [Fact]
    public async Task ReadsOnlyItsOwnRadioPort()
    {
        await using var client = new KissTcpClient("127.0.0.1", Port, tncPort: 1);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        using var tnc = await _tnc.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        byte[] heard = [.. Wire("N0CALL>APRS:>port 0", port: 0), .. Wire("N0CALL>APRS:>port 1", port: 1)];
        await tnc.GetStream().WriteAsync(heard, TestContext.Current.CancellationToken);
        tnc.Client.Shutdown(SocketShutdown.Send);

        var packets = new List<AprsPacket>();
        await foreach (var packet in client.ReadPacketsAsync(TestContext.Current.CancellationToken))
        {
            packets.Add(packet);
        }

        Assert.Equal(">port 1", System.Text.Encoding.Latin1.GetString(Assert.Single(packets).Information.Span));
    }

    [Fact]
    public async Task SendsAPacketAsAKissDataFrame()
    {
        await using var client = new KissTcpClient("127.0.0.1", Port);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        using var tnc = await _tnc.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        var packet = AprsPacket.Parse("N0CALL-9>APZ123,WIDE1-1:!4903.50N/07201.75W>Mobile");

        await client.SendAsync(packet, TestContext.Current.CancellationToken);

        var expected = Wire(packet.ToString());
        var received = new byte[expected.Length];
        await tnc.GetStream().ReadExactlyAsync(received, TestContext.Current.CancellationToken);
        Assert.Equal(expected, received);
    }

    [Fact]
    public async Task RefusesAPacketHeardOnAprsIs()
    {
        await using var client = new KissTcpClient("127.0.0.1", Port);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SendAsync(
                AprsPacket.Parse("N0CALL>APRS,TCPIP*,qAC,T2TEST:>Hi"),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task ReportsAnUnreachableTnc()
    {
        // A freed port is closed until something else binds it, and a parallel test occasionally does, so when the
        // connection is accepted, try a fresh port (as the APRS-IS transport's closed-port test does).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }

            await using var client = new KissTcpClient("127.0.0.1", port);
            try
            {
                await client.ConnectAsync(TestContext.Current.CancellationToken);
            }
            catch (SocketException)
            {
                return;
            }
        }

        Assert.Fail("Every freed port was taken before the client connected.");
    }

    [Fact]
    public async Task NeedsAConnectionBeforeReadingOrSending()
    {
        await using var client = new KissTcpClient("127.0.0.1", Port);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.SendAsync(AprsPacket.Parse("N0CALL>APRS:>Hi"), TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in client.ReadFramesAsync(TestContext.Current.CancellationToken)) { }
        });
    }

    [Theory]
    [InlineData("", 8001, 0)]
    [InlineData("tnc", 0, 0)]
    [InlineData("tnc", 8001, 16)]
    public void RefusesBadSettings(string host, int port, int tncPort)
    {
        Assert.ThrowsAny<ArgumentException>(() => new KissTcpClient(host, port, tncPort));
    }
}
