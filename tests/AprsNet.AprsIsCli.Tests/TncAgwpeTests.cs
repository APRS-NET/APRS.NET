using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli.Tests;

/// <summary><c>--tnc agw:host[:port]</c>: <c>tnc listen</c>, <c>tnc send</c> and <c>serve</c> against a fake AGWPE server.</summary>
public sealed class TncAgwpeTests : IDisposable
{
    private readonly TcpListener _server = new(IPAddress.Loopback, 0);

    public TncAgwpeTests() => _server.Start();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Address => $"agw:127.0.0.1:{((IPEndPoint)_server.LocalEndpoint).Port}";

    public void Dispose() => _server.Dispose();

    /// <summary>An AGWPE frame: the 36-byte header, then the data.</summary>
    private static byte[] Agw(int port, char kind, byte[] data)
    {
        var bytes = new byte[36 + data.Length];
        bytes[0] = (byte)port;
        bytes[4] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), data.Length);
        data.CopyTo(bytes, 36);
        return bytes;
    }

    private static byte[] Heard(int port, string line) =>
        Agw(port, 'K', [0, .. Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)).GetBytes()]);

    /// <summary>The kinds of the AGWPE frames in <paramref name="bytes"/>, with their ports.</summary>
    private static List<(char Kind, int Port, byte[] Data)> Frames(byte[] bytes)
    {
        var frames = new List<(char, int, byte[])>();
        for (var at = 0; at + 36 <= bytes.Length; )
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 28));
            frames.Add(((char)bytes[at + 4], bytes[at], bytes[(at + 36)..(at + 36 + length)]));
            at += 36 + length;
        }

        return frames;
    }

    [Theory]
    [InlineData("agw:localhost", "localhost", 8000)]
    [InlineData("AGW:10.0.0.5:8010", "10.0.0.5", 8010)]
    [InlineData("localhost", "localhost", 8001)]
    public void AnAgwAddressDefaultsToPort8000(string text, string host, int port)
    {
        Assert.True(TncAddress.TryParse(text, out var address));
        Assert.Equal(
            (host, port, text.StartsWith("agw", StringComparison.OrdinalIgnoreCase)),
            (address.Host, address.Port, address.Agwpe)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListenPrintsWhatTheServerHears(bool reconnect)
    {
        string[] args = ["tnc", "listen", "--tnc", Address, "-n", "2"];
        var run = new CliHarness().RunAsync(reconnect ? [.. args, "--reconnect"] : args, Token);
        using var server = await _server.AcceptTcpClientAsync(Token);
        var stream = server.GetStream();
        var requests = new byte[3 * 36];
        await stream.ReadExactlyAsync(requests, Token);
        await stream.WriteAsync(Agw(0, 'G', Encoding.ASCII.GetBytes("2;Port1 VHF;Port2 HF;")), Token);
        await stream.WriteAsync(Heard(1, "N2ABC>APRS:>on HF, not read"), Token);
        await stream.WriteAsync(Heard(0, "N0CALL-9>APRS,WIDE1-1:>One"), Token);
        await stream.WriteAsync(Heard(0, "N0CALL-9>APRS:>Two"), Token);

        var result = await run.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal(['R', 'G', 'k'], Frames(requests).Select(f => f.Kind));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            $"N0CALL-9>APRS,WIDE1-1:>One{Environment.NewLine}N0CALL-9>APRS:>Two{Environment.NewLine}",
            result.Out
        );
    }

    [Fact]
    public async Task ListenWithAllPortsSaysWhichPort()
    {
        var run = new CliHarness().RunAsync(["tnc", "listen", "--tnc", Address, "--all-ports", "-n", "2"], Token);
        using var server = await _server.AcceptTcpClientAsync(Token);
        byte[] heard = [.. Heard(0, "N1ABC>APRS:>VHF"), .. Heard(1, "N2ABC>APRS:>HF")];
        await server.GetStream().WriteAsync(heard, Token);

        var result = await run.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            ["N1ABC>APRS:>VHF", "[port 1] N2ABC>APRS:>HF"],
            result.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SendGoesOutAsARawFrameOnTheRadioPort(int radioPort)
    {
        var receiving = Task.Run(
            async () =>
            {
                using var server = await _server.AcceptTcpClientAsync(Token);
                using var received = new MemoryStream();
                await server.GetStream().CopyToAsync(received, Token);
                return received.ToArray();
            },
            Token
        );
        var result = await new CliHarness().RunAsync(
            ["tnc", "send", "status", "Testing", "-c", "KD9XYZ-9", "--tnc", Address, "--radio-port", $"{radioPort}"],
            Token
        );
        var sent = Frames(await receiving.WaitAsync(TimeSpan.FromSeconds(10), Token));

        Assert.Equal(0, result.ExitCode);
        var frame = Assert.Single(sent, f => f.Kind == 'K');
        Assert.Equal(radioPort, frame.Port);
        var line = result.Out.TrimEnd();
        Assert.Equal([0, .. Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)).GetBytes()], frame.Data);
    }

    [Fact]
    public async Task AnAgwpeServerThatIsNotThereIsReported()
    {
        var address = Address;
        _server.Stop();
        var result = await new CliHarness().RunAsync(["tnc", "listen", "--tnc", address, "-n", "1"], Token);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("could not connect to the TNC", result.Error, StringComparison.Ordinal);
    }
}
