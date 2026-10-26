using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

/// <summary>
/// One KISS connection for every radio port of a multi-port TNC: a fake two-port TNC, and Dire Wolf with two channels
/// and no audio.
/// </summary>
public sealed class KissMultiPortTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Ax25Frame Frame(string line) => Ax25Frame.FromAprsPacket(AprsPacket.Parse(line));

    private static byte[] Kiss(string line, int port) => KissFrame.ForAx25(Frame(line), port).Encode();

    [Fact]
    public async Task WithAllPortsEachFrameSaysWhichPortItWasHeardOn()
    {
        var tnc = new FakeTnc();
        await using var client = new KissStreamClient(tnc, transmitLimit: KissTransmitLimit.Off) { AllPorts = true };
        tnc.Send(Kiss("N1ABC>APRS:>on VHF", 0));
        tnc.Send(Kiss("N2ABC>APRS:>on HF", 1));
        tnc.Send(Kiss("N3ABC>APRS:>on the last port", 15));

        var heard = new List<KissPortFrame>();
        await foreach (var frame in client.ReadPortFramesAsync(Token).WithCancellation(Token))
        {
            heard.Add(frame);
            if (heard.Count == 3)
            {
                break;
            }
        }

        Assert.Equal([0, 1, 15], heard.Select(f => f.Port));
        Assert.Equal("N2ABC>APRS:>on HF", heard[1].Frame.ToTnc2());
    }

    [Fact]
    public async Task WithoutAllPortsOnlyItsOwnPortIsRead()
    {
        var tnc = new FakeTnc();
        await using var client = new KissStreamClient(tnc, tncPort: 1, transmitLimit: KissTransmitLimit.Off);
        tnc.Send(Kiss("N1ABC>APRS:>on VHF", 0));
        tnc.Send(Kiss("N2ABC>APRS:>on HF", 1));

        await using var frames = client.ReadPortFramesAsync(Token).GetAsyncEnumerator(Token);
        Assert.True(await frames.MoveNextAsync().AsTask().WaitAsync(Wait, Token));

        Assert.Equal(new KissPortFrame(1, Frame("N2ABC>APRS:>on HF")).Port, frames.Current.Port);
        Assert.Equal("N2ABC>APRS:>on HF", frames.Current.Frame.ToTnc2());
    }

    [Fact]
    public async Task FramesAreSentOnTheRadioPortAsked()
    {
        var tnc = new FakeTnc();
        await using var client = new KissStreamClient(tnc, tncPort: 1, transmitLimit: KissTransmitLimit.Off);

        await client.SendAsync(AprsPacket.Parse("N0CALL>APZNET:>own port"), Token);
        await client.SendToPortAsync(0, AprsPacket.Parse("N0CALL>APZNET:>port 0"), Token);
        await client.SendFrameToPortAsync(7, Frame("N0CALL>APZNET:>port 7"), Token);

        var decoder = new KissDecoder();
        var sent = decoder.Decode(tnc.Written.ToArray());
        Assert.Equal([1, 0, 7], sent.Select(f => f.Port));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await client.SendToPortAsync(16, AprsPacket.Parse("N0CALL>APZNET:>no"), Token)
        );
    }

    [Fact]
    public async Task EachPortHasATransmitLimitOfItsOwn()
    {
        var time = new FakeTimeProvider();
        var tnc = new FakeTnc();
        await using var client = new KissStreamClient(
            tnc,
            transmitLimit: new KissTransmitLimit(burst: 1, TimeSpan.FromSeconds(10), time)
        );

        await client.SendToPortAsync(0, AprsPacket.Parse("N0CALL>APZNET:>first on 0"), Token);
        // Port 0 has used its burst; port 1 hasn't.
        await client
            .SendToPortAsync(1, AprsPacket.Parse("N0CALL>APZNET:>first on 1"), Token)
            .AsTask()
            .WaitAsync(Wait, Token);
        var second = client.SendToPortAsync(0, AprsPacket.Parse("N0CALL>APZNET:>second on 0"), Token).AsTask();
        await Task.Delay(100, Token);
        Assert.False(second.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(10));
        await second.WaitAsync(Wait, Token);
    }

    [Fact]
    public async Task ALastingConnectionReadsAndSendsOnEveryPort()
    {
        var tnc = new FakeTnc();
        await using var connection = new KissTcpConnection(
            _ => Task.FromResult<Stream>(tnc),
            new KissTcpOptions
            {
                Host = "twoport",
                AllPorts = true,
                TransmitLimit = KissTransmitLimit.Off,
            }
        );
        tnc.Send(Kiss("N2ABC>APRS:>on HF", 1));

        await using var frames = connection.ReadPortFramesAsync(Token).GetAsyncEnumerator(Token);
        Assert.True(await frames.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        Assert.Equal(1, frames.Current.Port);
        Assert.True(connection.AllPorts);

        await connection.SendToPortAsync(1, AprsPacket.Parse("N0CALL>APZNET:>reply on HF"), Token);
        Assert.Equal(1, Assert.Single(new KissDecoder().Decode(tnc.Written.ToArray())).Port);
    }

    [Fact]
    public async Task FramesAreCountedByPort()
    {
        var tnc = new FakeTnc();
        var host = "p" + Guid.NewGuid().ToString("N")[..8];
        var seen = new ConcurrentBag<(string Instrument, string? Port)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == KissMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, _, tags, _) =>
            {
                string? tnc = null;
                string? port = null;
                foreach (var tag in tags)
                {
                    (tnc, port) = tag.Key switch
                    {
                        "tnc" => (tag.Value as string, port),
                        "port" => (tnc, tag.Value as string),
                        _ => (tnc, port),
                    };
                }

                if (tnc == host)
                {
                    seen.Add((instrument.Name, port));
                }
            }
        );
        listener.Start();
        await using var client = new KissStreamClient(tnc, transmitLimit: KissTransmitLimit.Off)
        {
            AllPorts = true,
            MetricsTnc = host,
        };
        tnc.Send(Kiss("N2ABC>APRS:>on HF", 1));
        await using var frames = client.ReadPortFramesAsync(Token).GetAsyncEnumerator(Token);
        Assert.True(await frames.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        await client.SendToPortAsync(2, AprsPacket.Parse("N0CALL>APZNET:>on 2"), Token);

        Assert.Contains(("aprsnet.kiss.frames.received", "1"), seen);
        Assert.Contains(("aprsnet.kiss.frames.sent", "2"), seen);
    }

    /// <summary>
    /// Dire Wolf with two channels and no audio: a station's frame sent on channel 1 reaches the all-ports connection
    /// as port 1, and the reply sent on port 1 reaches the station on channel 1 (<c>KISSCOPY</c>). Nothing reaches a
    /// radio. Opt-in, as the audio tests: <c>APRSNET_DIREWOLF_AUDIO=1</c> with <c>direwolf</c> on the PATH.
    /// </summary>
    [Fact]
    public async Task ReadsAndSendsOnBothChannelsOfDireWolf()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("APRSNET_DIREWOLF_AUDIO") != "1",
            "Set APRSNET_DIREWOLF_AUDIO=1 with direwolf on the PATH to run against Dire Wolf with two channels."
        );
        var directory = Directory.CreateTempSubdirectory("aprsnet-twoport-");
        var port = FreePort();
        var config = Path.Combine(directory.FullName, "direwolf.conf");
        await File.WriteAllLinesAsync(
            config,
            [
                "ADEVICE null null",
                "ACHANNELS 2",
                "CHANNEL 0",
                "MYCALL N0DW",
                "CHANNEL 1",
                "MYCALL N0DW-1",
                $"KISSPORT {port}",
                "AGWPORT 0",
                "KISSCOPY 1",
            ],
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
            await using var station = await ConnectAsync(port, allPorts: true);
            await using var connection = new KissTcpConnection(
                new KissTcpOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    AllPorts = true,
                    TransmitLimit = KissTransmitLimit.Off,
                }
            );
            await using var heard = connection.ReadPortFramesAsync(Token).GetAsyncEnumerator(Token);
            var next = heard.MoveNextAsync().AsTask();
            var deadline = DateTime.UtcNow + Wait;
            while (!connection.IsConnected)
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out connecting to Dire Wolf.");
                await Task.Delay(50, Token);
            }

            await station.SendFrameToPortAsync(1, Frame("N1ABC>APRS:>on channel 1"), Token);
            Assert.True(await next.WaitAsync(Wait, Token));
            Assert.Equal(1, heard.Current.Port);
            Assert.Equal("N1ABC>APRS:>on channel 1", heard.Current.Frame.ToTnc2());

            var reply = station.ReadPortFramesAsync(Token).GetAsyncEnumerator(Token);
            await connection.SendToPortAsync(1, AprsPacket.Parse("N0CALL>APZNET:>back on channel 1"), Token);
            Assert.True(await reply.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
            Assert.Equal(1, reply.Current.Port);
            Assert.Equal("N0CALL>APZNET:>back on channel 1", reply.Current.Frame.ToTnc2());
        }
        finally
        {
            direwolf.Kill();
            await direwolf.WaitForExitAsync(CancellationToken.None);
            directory.Delete(recursive: true);
        }
    }

    private static async Task<KissTcpClient> ConnectAsync(int port, bool allPorts)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (true)
        {
            var client = new KissTcpClient(
                new KissTcpOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    AllPorts = allPorts,
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
