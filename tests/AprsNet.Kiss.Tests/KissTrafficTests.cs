using System.Text;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

/// <summary>The <c>Traffic</c> event: everything that crosses a KISS link, for a troubleshooting console.</summary>
public sealed class KissTrafficTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static byte[] Text(string text) => Encoding.Latin1.GetBytes(text);

    private static byte[] Kiss(string line, int port = 0) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)), port).Encode();

    /// <summary>A valid AX.25 UI frame with another protocol (PID CF, NET/ROM), so not APRS.</summary>
    private static byte[] NotAprs()
    {
        var ax25 = Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL>APRS:>netrom")).GetBytes();
        ax25[15] = 0xCF; // the PID, after two 7-byte addresses and the control byte
        return new KissFrame(0, KissCommand.Data, ax25).Encode();
    }

    private static List<KissTrafficEventArgs> Record(KissStreamClient client)
    {
        var entries = new List<KissTrafficEventArgs>();
        client.Traffic += (_, e) =>
        {
            lock (entries)
            {
                entries.Add(e);
            }
        };
        return entries;
    }

    private static async Task<List<Ax25Frame>> ReadAll(KissStreamClient client)
    {
        var frames = new List<Ax25Frame>();
        await foreach (var frame in client.ReadFramesAsync(TestContext.Current.CancellationToken))
        {
            frames.Add(frame);
        }

        return frames;
    }

    [Fact]
    public async Task EachKindOfTrafficIsReportedOnceWithWhatItIs()
    {
        var tnc = new FakeTnc();
        await using var client = new KissStreamClient(tnc, transmitLimit: KissTransmitLimit.Off);
        var entries = Record(client);

        tnc.Send(Text("cmd:KISS ON\r\nKISS     was OFF\r\ncmd:"));
        tnc.Send([0xC0, 0x00, 0x82, 0xDB, 0x41, 0x42, 0xC0]); // an escape byte followed by 0x41
        tnc.Send([0xC0, 0x00, .. Enumerable.Repeat((byte)0x80, 2100), 0xC0]); // longer than any frame
        tnc.Send(NotAprs());
        tnc.Send(Kiss("N0CALL>APRS:>for port 1", port: 1));
        tnc.Send([0xC0, 0x01, 0x32, 0xC0]); // TXDELAY, a command, not data
        tnc.Send(Kiss("N0CALL>APRS:>an APRS frame"));
        tnc.End();
        var frames = await ReadAll(client);

        Assert.Equal(["N0CALL>APRS:>netrom", "N0CALL>APRS:>an APRS frame"], frames.Select(f => f.ToTnc2()));
        Assert.Equal(
            [
                KissTrafficKind.Unframed,
                KissTrafficKind.Unframed,
                KissTrafficKind.Unframed,
                KissTrafficKind.DroppedFrame,
                KissTrafficKind.DroppedFrame,
                KissTrafficKind.NotAprs,
                KissTrafficKind.OtherPort,
                KissTrafficKind.Command,
                KissTrafficKind.Frame,
            ],
            entries.Select(e => e.Kind)
        );

        // The text a TNC prints at its prompt reads like a terminal: a line at a time, the prompt on its own.
        Assert.Equal(["cmd:KISS ON\r\n", "KISS     was OFF\r\n", "cmd:"], entries.Take(3).Select(e => e.Text));
        Assert.Equal([true, true, false], entries.Take(3).Select(e => e.EndsLine));
        Assert.All(entries.Take(3), e => Assert.True(e.LooksLikeText));

        Assert.Equal(KissDropReason.InvalidEscape, entries[3].DropReason);
        Assert.Equal(new byte[] { 0xC0, 0x00, 0x82, 0xDB, 0x41, 0x42, 0xC0 }, entries[3].Bytes.ToArray());
        Assert.Equal(KissDropReason.TooLong, entries[4].DropReason);
        Assert.Equal((byte)0xCF, entries[5].Frame!.Protocol);
        Assert.Null(entries[5].Packet);
        Assert.Equal(1, entries[6].Port);
        Assert.Equal(KissCommand.TxDelay, entries[7].Command);
        Assert.Equal("N0CALL>APRS:>an APRS frame", entries[8].Packet!.ToString());
        Assert.Equal(Kiss("N0CALL>APRS:>an APRS frame"), entries[8].Bytes.ToArray());
        Assert.All(entries, e => Assert.Equal(KissTrafficDirection.Received, e.Direction));
    }

    [Fact]
    public async Task ASentFrameIsReported()
    {
        var tnc = new FakeTnc();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 17, 22, 0, 0, TimeSpan.Zero));
        await using var client = new KissStreamClient(tnc, transmitLimit: KissTransmitLimit.Off)
        {
            TimeProvider = time,
        };
        var entries = Record(client);

        await client.SendAsync(AprsPacket.Parse("N0CALL>APZNET:>on the air"), TestContext.Current.CancellationToken);

        var sent = Assert.Single(entries);
        Assert.Equal((KissTrafficKind.Sent, KissTrafficDirection.Sent), (sent.Kind, sent.Direction));
        Assert.Equal(tnc.Written.ToArray(), sent.Bytes.ToArray());
        Assert.Equal("N0CALL>APZNET:>on the air", sent.Packet!.ToString());
        Assert.Equal(time.GetUtcNow(), sent.At);
    }

    [Fact]
    public async Task TextAfterAFrameIsTextNotTheStartOfAFrame()
    {
        // A TNC that lost KISS mode (a radio after a power cut): text follows the last frame's FEND.
        var tnc = new FakeTnc();
        await using var client = new KissStreamClient(tnc, transmitLimit: KissTransmitLimit.Off);
        var entries = Record(client);
        var text = string.Concat(Enumerable.Repeat("N0CALL-9>APRS,WIDE1-1:!4903.50N/07201.75W-printed as text\r\n", 3));

        tnc.Send(Kiss("N0CALL>APRS:>last KISS frame"));
        tnc.Send(Text(text));
        tnc.End();
        await ReadAll(client);

        Assert.Equal(KissTrafficKind.Frame, entries[0].Kind);
        Assert.Equal(text, string.Concat(entries.Skip(1).Select(e => e.Text)));
        Assert.All(entries.Skip(1), e => Assert.Equal(KissTrafficKind.Unframed, e.Kind));
        Assert.Equal(0, client.DroppedFrames);
    }

    [Fact]
    public async Task NothingIsKeptOrReportedWhileNoOneListens()
    {
        var decoder = new KissDecoder();
        decoder.Decode([.. Text("cmd:"), 0xC0, 0x00, 0x82, 0xA0]);
        Assert.Equal(0, decoder.RawBuffered);

        // A forwarding subscription alone (a TCP client inside a connection) doesn't turn tracing on.
        var tnc = new FakeTnc();
        await using var client = new KissStreamClient(tnc, transmitLimit: KissTransmitLimit.Off)
        {
            WantsTraffic = () => false,
        };
        var entries = Record(client);
        tnc.Send(Text("cmd:"));
        tnc.Send(Kiss("N0CALL>APRS:>quiet"));
        tnc.End();
        await ReadAll(client);
        await client.SendAsync(AprsPacket.Parse("N0CALL>APZNET:>x"), TestContext.Current.CancellationToken);

        Assert.Empty(entries);
    }

    [Fact]
    public async Task AHandlerThatThrowsDoesNotStopReading()
    {
        var tnc = new FakeTnc();
        await using var client = new KissStreamClient(tnc, transmitLimit: KissTransmitLimit.Off);
        client.Traffic += (_, _) => throw new InvalidOperationException("console bug");

        tnc.Send(Kiss("N0CALL>APRS:>one"));
        tnc.Send(Kiss("N0CALL>APRS:>two"));
        tnc.End();

        Assert.Equal(2, (await ReadAll(client)).Count);
    }

    [Fact]
    public async Task TheConnectionReportsTrafficAndItsSetupSteps()
    {
        var time = new FakeTimeProvider();
        var radio = new FakeTnc();
        radio.Answer = written =>
            Encoding.Latin1.GetString(written) switch
            {
                "\r" => Text("\r\ncmd:"),
                "KISS ON\r" => Text("KISS ON\r\nKISS     was OFF\r\ncmd:"),
                "RESTART\r" => Kiss("N0CALL>APRS:>after the setup"),
                _ => null,
            };
        await using var connection = new KissSerialConnection(
            _ => Task.FromResult<Stream>(radio),
            new KissSerialOptions
            {
                PortName = "/dev/ttyUSB0",
                TimeProvider = time,
                Setup = TncSetup.KenwoodThD72,
            }
        );
        var entries = new List<KissTrafficEventArgs>();
        connection.Traffic += (_, e) =>
        {
            lock (entries)
            {
                entries.Add(e);
            }
        };
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var next = packets.MoveNextAsync().AsTask();
        for (var i = 0; i < 1000 && !next.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        Assert.True(await next.WaitAsync(Wait, TestContext.Current.CancellationToken));
        List<KissTrafficEventArgs> seen;
        lock (entries)
        {
            seen = [.. entries];
        }

        var setup = seen.TakeWhile(e => e.Kind != KissTrafficKind.Frame).ToList();
        Assert.Equal(
            "\xC0\xFF\xC0\rKISS ON\rRESTART\r",
            string.Concat(setup.Where(e => e.Kind == KissTrafficKind.SetupSent).Select(e => e.Text))
        );
        Assert.Contains(
            setup,
            e => e is { Kind: KissTrafficKind.SetupReceived, Text: "KISS ON\r\nKISS     was OFF\r\ncmd:" }
        );
        Assert.All(setup, e => Assert.NotNull(e.SetupStep));
        Assert.Equal(
            "N0CALL>APRS:>after the setup",
            seen.First(e => e.Kind == KissTrafficKind.Frame).Packet!.ToString()
        );
    }

    [Fact]
    public void EveryKindHasADescription()
    {
        foreach (var kind in Enum.GetValues<KissTrafficKind>())
        {
            Assert.NotEqual(kind.ToString(), kind.Describe());
        }
    }
}
