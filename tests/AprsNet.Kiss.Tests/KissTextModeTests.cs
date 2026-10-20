using System.Text;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

/// <summary>
/// Text mode: a radio in its APRS mode prints each packet it hears as a TNC-2 line, with GPS sentences between them,
/// and lines sometimes run together where two collide (seen on a TH-D72A; these lines are made up).
/// </summary>
public sealed class KissTextModeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // A textbook NMEA sentence, not any station's position.
    private const string Gga = "$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,*47";

    private static byte[] Text(string text) => Encoding.Latin1.GetBytes(text);

    private static KissSerialConnection Connection(FakeTnc tnc) =>
        new(
            _ => Task.FromResult<Stream>(tnc),
            new KissSerialOptions
            {
                PortName = "/dev/ttyUSB0",
                TimeProvider = new FakeTimeProvider(),
                TextMode = true,
            }
        );

    private static async Task<List<AprsPacket>> ReadUntilEnd(KissConnection connection, int count)
    {
        var packets = new List<AprsPacket>();
        await foreach (var packet in connection.ReadPacketsAsync(TestContext.Current.CancellationToken))
        {
            packets.Add(packet);
            if (packets.Count == count)
            {
                break;
            }
        }

        return packets;
    }

    [Fact]
    public async Task PacketLinesAreReadAndGpsSentencesSkipped()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(tnc);
        var traffic = new List<KissTrafficEventArgs>();
        connection.Traffic += (_, e) =>
        {
            lock (traffic)
            {
                traffic.Add(e);
            }
        };

        tnc.Send(Text($"N0CALL-9>APRS,WIDE1-1:!4903.50N/07201.75W-on the air\r\n{Gga}\r\nKD9XY"));
        tnc.Send(Text("Z>APDW17,K0ABC-1*,WIDE2-1:>split across two reads\r\n"));
        tnc.Send(Text($"N0CALL-9>APRS:>cut short{Gga}\r\n")); // a sentence run into a packet line
        tnc.Send(Text("N0CALL-16>APRS:>an SSID AX.25 can't carry\r\n"));
        tnc.Send(Text("W1AW>APRS:>last\r\n"));

        var packets = await ReadUntilEnd(connection, 3).WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "N0CALL-9>APRS,WIDE1-1:!4903.50N/07201.75W-on the air",
                "KD9XYZ>APDW17,K0ABC-1*,WIDE2-1:>split across two reads",
                "W1AW>APRS:>last",
            ],
            packets.Select(p => p.ToString())
        );
        lock (traffic)
        {
            Assert.Equal(
                [
                    KissTrafficKind.Frame,
                    KissTrafficKind.Unframed,
                    KissTrafficKind.Frame,
                    KissTrafficKind.DroppedFrame,
                    KissTrafficKind.DroppedFrame,
                    KissTrafficKind.Frame,
                ],
                traffic.Select(e => e.Kind)
            );
            Assert.Equal(Gga, traffic[1].Text);
            Assert.Equal(
                [KissDropReason.DamagedLine, KissDropReason.NotAx25],
                traffic.Where(e => e.DropReason is not null).Select(e => e.DropReason!.Value)
            );
        }
    }

    /// <summary>
    /// Five minutes recorded from a TH-D72A in APRS mode with its GPS output on (2026-10-20), each GPS position and
    /// altitude replaced with APRS101's example, fed in as a serial port reads it. One packet ran into the middle of a
    /// satellite sentence: that line starts as a sentence, so it is skipped with the GPS output and gives no packet.
    /// </summary>
    [Fact]
    public async Task ARecordingFromATHD72AGivesItsPacketsAndSkipsTheGpsOutput()
    {
        var recording = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Recordings", "th-d72a-aprs-mode.txt")
        );
        var lines = Encoding.Latin1.GetString(recording).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        string[] expected =
        [
            .. lines.Where(line => !line.StartsWith('$') && !line.Contains("$GP", StringComparison.Ordinal)),
            "W1AW>APRS:>end of recording",
        ];
        var tnc = new FakeTnc();
        await using var connection = Connection(tnc);
        var traffic = new List<KissTrafficEventArgs>();
        connection.Traffic += (_, e) =>
        {
            lock (traffic)
            {
                traffic.Add(e);
            }
        };
        foreach (var chunk in recording.Chunk(64))
        {
            tnc.Send(chunk);
        }

        tnc.Send(Text("W1AW>APRS:>end of recording\r\n"));

        var packets = await ReadUntilEnd(connection, expected.Length)
            .WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(17, expected.Length);
        Assert.Equal(expected, packets.Select(p => p.ToString()));
        Assert.All(packets, p => Assert.DoesNotContain(p.Issues, i => i.Severity == ParseIssueSeverity.Error));
        lock (traffic)
        {
            Assert.DoesNotContain(traffic, e => e.DropReason is not null);
            Assert.Equal(1359, traffic.Count(e => e.Kind == KissTrafficKind.Unframed && e.Text.StartsWith('$')));
        }
    }

    [Fact]
    public async Task ARadioInAprsModeTakesNothingToTransmit()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(tnc);
        tnc.Send(Text("W1AW>APRS:>heard\r\n"));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await packets.MoveNextAsync());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.SendAsync(AprsPacket.Parse("N0CALL>APZNET:>no"), TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Contains("KISS mode to send", ex.Message, StringComparison.Ordinal);
        Assert.Empty(tnc.Written.ToArray());
    }

    [Fact]
    public void TextModeNeedsNoSetup() =>
        Assert.Throws<ArgumentException>(() =>
            new KissSerialConnection(
                new KissSerialOptions
                {
                    PortName = "/dev/ttyUSB0",
                    TextMode = true,
                    Setup = TncSetup.KenwoodThD72,
                }
            )
        );

    [Fact]
    public async Task TextIsNotTakenForAKissTncInTheWrongMode()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(tnc);
        var warned = false;
        connection.Warning += (_, _) => warned = true;
        tnc.Send(
            Text(string.Concat(Enumerable.Repeat("W1AW>APRS:>a packet line long enough to look like text\r\n", 4)))
        );

        var packets = await ReadUntilEnd(connection, 4).WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(4, packets.Count);
        Assert.False(warned);
    }
}
