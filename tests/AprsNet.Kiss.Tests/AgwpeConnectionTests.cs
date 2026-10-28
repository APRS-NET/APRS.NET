using System.Buffers.Binary;
using System.Text;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

/// <summary>
/// <see cref="AgwpeConnection"/> against a fake AGWPE server. The frames are built here byte by byte, in the layout of
/// the AGWPE TCP/IP API; the version and port list are as Dire Wolf 1.8 sends them (captured from its AGW port).
/// </summary>
public sealed class AgwpeConnectionTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _time = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>An AGWPE frame: the 36-byte header, then the data.</summary>
    private static byte[] Agw(int port, char kind, byte[] data, string from = "", string to = "")
    {
        var bytes = new byte[36 + data.Length];
        bytes[0] = (byte)port;
        bytes[4] = (byte)kind;
        Encoding.ASCII.GetBytes(from, bytes.AsSpan(8));
        Encoding.ASCII.GetBytes(to, bytes.AsSpan(18));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), data.Length);
        data.CopyTo(bytes, 36);
        return bytes;
    }

    // Dire Wolf's answers to R and G, byte for byte.
    private static readonly byte[] DireWolfVersion = Convert.FromHexString(
        "000000005200000000000000000000000000000000000000000000000800000000000000d50700007f000000"
    );

    private static readonly byte[] DireWolfPorts = Agw(
        0,
        'G',
        Encoding.ASCII.GetBytes("1;Port1 first soundcard mono;\0")
    );

    /// <summary>A raw frame heard on <paramref name="port"/>: the command byte 0, then the AX.25 frame.</summary>
    private static byte[] Heard(int port, string line)
    {
        var ax25 = Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)).GetBytes();
        return Agw(port, 'K', [0, .. ax25], from: line[..line.IndexOf('>')]);
    }

    private AgwpeConnection Connect(Queue<FakeTnc> servers, bool allPorts = false) =>
        new(
            _ => Task.FromResult<Stream>(servers.Dequeue()),
            new AgwpeOptions
            {
                Host = "agwpe",
                AllPorts = allPorts,
                ReconnectDelay = TimeSpan.FromSeconds(5),
                MaxReconnectDelay = TimeSpan.FromSeconds(5),
                TimeProvider = _time,
                TransmitLimit = KissTransmitLimit.Off,
            }
        );

    /// <summary>The AGWPE frames the client wrote, as (port, kind, data).</summary>
    private static List<(int Port, char Kind, byte[] Data)> Written(FakeTnc server)
    {
        var bytes = server.Written.ToArray();
        var frames = new List<(int, char, byte[])>();
        for (var at = 0; at + 36 <= bytes.Length; )
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 28));
            frames.Add((bytes[at], (char)bytes[at + 4], bytes[(at + 36)..(at + 36 + length)]));
            at += 36 + length;
        }

        return frames;
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            await Task.Delay(10, Token);
        }
    }

    [Fact]
    public async Task ItAsksForTheVersionThePortsAndRawFrames()
    {
        var server = new FakeTnc();
        await using var tnc = Connect(new([server]));
        server.Send(DireWolfVersion);
        server.Send(DireWolfPorts);
        var ports = new List<IReadOnlyList<AgwpePort>>();
        tnc.PortsReceived += (_, e) => ports.Add(e.Ports);
        server.Send(Heard(0, "N1ABC>APRS,WIDE1-1:>hello"));

        await using var packets = tnc.ReadPacketsAsync(Token).GetAsyncEnumerator(Token);
        Assert.True(await packets.MoveNextAsync().AsTask().WaitAsync(Wait, Token));

        Assert.Equal([('R', 0), ('G', 0), ('k', 0)], Written(server).Select(f => (f.Kind, f.Data.Length)));
        Assert.Equal("2005.127", tnc.ServerVersion);
        Assert.Equal([new AgwpePort(0, "first soundcard mono")], tnc.Ports);
        Assert.Equal(tnc.Ports, Assert.Single(ports));
        Assert.Equal("N1ABC>APRS,WIDE1-1:>hello", packets.Current.ToString());
    }

    [Fact]
    public async Task OnlyItsOwnPortIsReadUnlessAllPorts()
    {
        var server = new FakeTnc();
        await using var tnc = Connect(new([server]));
        server.Send(Heard(1, "N2ABC>APRS:>on port 1"));
        server.Send(Heard(0, "N1ABC>APRS:>on port 0"));

        await using var frames = tnc.ReadPortFramesAsync(Token).GetAsyncEnumerator(Token);
        Assert.True(await frames.MoveNextAsync().AsTask().WaitAsync(Wait, Token));

        Assert.Equal(0, frames.Current.Port);
        Assert.Equal("N1ABC>APRS:>on port 0", frames.Current.Frame.ToTnc2());
    }

    [Fact]
    public async Task WithAllPortsEachFrameSaysItsPort()
    {
        var server = new FakeTnc();
        await using var tnc = Connect(new([server]), allPorts: true);
        server.Send(Agw(0, 'G', Encoding.ASCII.GetBytes("2;Port1 VHF 1200;Port2 HF 300;")));
        server.Send(Heard(1, "N2ABC>APRS:>on HF"));
        server.Send(Heard(0, "N1ABC>APRS:>on VHF"));

        var heard = new List<KissPortFrame>();
        await foreach (var frame in tnc.ReadPortFramesAsync(Token).WithCancellation(Token))
        {
            heard.Add(frame);
            if (heard.Count == 2)
            {
                break;
            }
        }

        Assert.Equal([1, 0], heard.Select(f => f.Port));
        Assert.Equal([new AgwpePort(0, "VHF 1200"), new AgwpePort(1, "HF 300")], tnc.Ports);
    }

    [Fact]
    public async Task FramesAreSentAsRawFramesOnTheirPort()
    {
        var server = new FakeTnc();
        await using var tnc = Connect(new([server]), allPorts: true);
        await using var frames = tnc.ReadPortFramesAsync(Token).GetAsyncEnumerator(Token);
        var next = frames.MoveNextAsync().AsTask();
        await Until(() => tnc.IsConnected);

        var packet = AprsPacket.Parse("N0CALL>APZNET,WIDE1-1:>on air");
        await tnc.SendAsync(packet, Token);
        await tnc.SendToPortAsync(1, AprsPacket.Parse("N0CALL>APZNET:>on HF"), Token);

        var sent = Written(server).Where(f => f.Kind == 'K').ToList();
        Assert.Equal([0, 1], sent.Select(f => f.Port));
        Assert.Equal([0, .. Ax25Frame.FromAprsPacket(packet).GetBytes()], sent[0].Data);
        Assert.False(next.IsCompleted);
        await tnc.DisposeAsync(); // ends the read
        Assert.False(await next.WaitAsync(Wait, Token));
    }

    [Fact]
    public async Task ADamagedFrameIsDroppedAndTheReadGoesOn()
    {
        var server = new FakeTnc();
        await using var tnc = Connect(new([server]));
        server.Send(Agw(0, 'K', [0, 1, 2, 3]));
        server.Send(Agw(0, 'U', Encoding.ASCII.GetBytes(" 1:Fm N1ABC To APRS <UI pid=F0 Len=5 >\r>text\r"))); // monitoring
        server.Send(Heard(0, "N1ABC>APRS:>after"));

        await using var packets = tnc.ReadPacketsAsync(Token).GetAsyncEnumerator(Token);
        Assert.True(await packets.MoveNextAsync().AsTask().WaitAsync(Wait, Token));

        Assert.Equal("N1ABC>APRS:>after", packets.Current.ToString());
    }

    [Fact]
    public async Task AServerThatIsNotAgwpeIsDroppedAndTriedAgain()
    {
        var kiss = new FakeTnc();
        var agwpe = new FakeTnc();
        await using var tnc = Connect(new([kiss, agwpe]));
        var states = new List<KissConnectionStateChangedEventArgs>();
        tnc.StateChanged += (_, e) => states.Add(e);
        // A KISS TNC on the AGWPE port: its bytes make a header with an impossible length.
        kiss.Send([0xC0, 0x00, .. Enumerable.Repeat((byte)0xFF, 40)]);
        agwpe.Send(Heard(0, "N1ABC>APRS:>found"));

        await using var packets = tnc.ReadPacketsAsync(Token).GetAsyncEnumerator(Token);
        var next = packets.MoveNextAsync().AsTask();
        await Until(() => states.Any(s => s.State == KissConnectionState.Reconnecting));
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await next.WaitAsync(Wait, Token));

        Assert.IsType<IOException>(states.First(s => s.State == KissConnectionState.Reconnecting).Error);
        Assert.Equal("N1ABC>APRS:>found", packets.Current.ToString());
    }

    [Fact]
    public async Task AfterAReconnectItAsksAgainAndKeepsThePortsMeanwhile()
    {
        var first = new FakeTnc();
        var second = new FakeTnc();
        await using var tnc = Connect(new([first, second]));
        first.Send(DireWolfPorts);
        first.Send(Heard(0, "N1ABC>APRS:>first"));
        first.End();
        second.Send(Heard(0, "N1ABC>APRS:>second"));

        await using var packets = tnc.ReadPacketsAsync(Token).GetAsyncEnumerator(Token);
        Assert.True(await packets.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        var next = packets.MoveNextAsync().AsTask();
        await Until(() => tnc.State == KissConnectionState.Reconnecting);
        Assert.Single(tnc.Ports);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await next.WaitAsync(Wait, Token));

        Assert.Equal("N1ABC>APRS:>second", packets.Current.ToString());
        Assert.Equal(1, tnc.Reconnects);
        Assert.Equal(['R', 'G', 'k'], Written(second).Select(f => f.Kind));
    }

    [Fact]
    public async Task AQuietServerIsReported()
    {
        var server = new FakeTnc();
        await using var tnc = new AgwpeConnection(
            _ => Task.FromResult<Stream>(server),
            new AgwpeOptions { TimeProvider = _time, QuietWarning = TimeSpan.FromMinutes(30) }
        );
        await using var frames = tnc.ReadFramesAsync(Token).GetAsyncEnumerator(Token);
        var next = frames.MoveNextAsync().AsTask();
        await Until(() => tnc.IsConnected);

        _time.Advance(TimeSpan.FromMinutes(30));

        await Until(() => tnc.IsQuiet);
        await tnc.DisposeAsync();
        Assert.False(await next.WaitAsync(Wait, Token));
    }

    [Theory]
    [InlineData("", 8000)]
    [InlineData("localhost", 0)]
    public void UnusableSettingsAreRefused(string host, int port)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AgwpeConnection(host, port));
    }
}
