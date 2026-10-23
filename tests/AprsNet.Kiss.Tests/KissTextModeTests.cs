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

    private static KissSerialConnection Connection(FakeTnc tnc, TimeProvider? clock = null) =>
        new(
            _ => Task.FromResult<Stream>(tnc),
            new KissSerialOptions
            {
                PortName = "/dev/ttyUSB0",
                TimeProvider = clock ?? new FakeTimeProvider(),
                TextMode = true,
            }
        );

    /// <summary>A sentence with its checksum worked out: <c>$</c>, the body, <c>*</c> and two hex digits.</summary>
    private static string Sentence(string body) => $"${body}*{body.Aggregate(0, (sum, c) => sum ^ c):X2}";

    private static List<T> Record<T>(Action<EventHandler<T>> subscribe)
    {
        var list = new List<T>();
        subscribe(
            (_, e) =>
            {
                lock (list)
                {
                    list.Add(e);
                }
            }
        );
        return list;
    }

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
    public async Task PacketLinesAreReadAndGpsSentencesPassedOn()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(tnc);
        var sentences = Record<NmeaSentenceEventArgs>(h => connection.NmeaSentenceReceived += h);
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
                    KissTrafficKind.Nmea,
                    KissTrafficKind.Frame,
                    KissTrafficKind.DroppedFrame,
                    KissTrafficKind.DroppedFrame,
                    KissTrafficKind.Frame,
                ],
                traffic.Select(e => e.Kind)
            );
            Assert.Equal(Gga, traffic[1].Text);
            Assert.Equal("GGA", traffic[1].Sentence!.Type);
            Assert.Equal(
                [KissDropReason.DamagedLine, KissDropReason.NotAx25],
                traffic.Where(e => e.DropReason is not null).Select(e => e.DropReason!.Value)
            );
        }

        lock (sentences)
        {
            Assert.Equal([Gga], sentences.Select(e => e.Sentence.Text));
        }
    }

    [Fact]
    public async Task ASentenceThatIsCutOffOrHasAWrongChecksumIsDamagedNotPassedOn()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(tnc);
        var sentences = Record<NmeaSentenceEventArgs>(h => connection.NmeaSentenceReceived += h);
        var traffic = Record<KissTrafficEventArgs>(h => connection.Traffic += h);
        var waypoint = Sentence("PKWDWPL,232509.65,V,4903.50,N,07201.75,W,,,201026,,N0CALL-9,/>");

        tnc.Send(Text($"$GPVTG,096.61${Gga}\r\n")); // the next sentence ran into it, as a TH-D72A's sometimes do
        tnc.Send(Text($"{Gga[..^1]}0\r\n")); // a wrong checksum
        tnc.Send(Text($"{Gga[..^3]}\r\n")); // none at all
        tnc.Send(Text($"{waypoint}\r\n")); // a Kenwood's own sentence is a sentence too
        tnc.Send(Text("W1AW>APRS:>last\r\n"));
        await ReadUntilEnd(connection, 1).WaitAsync(Wait, TestContext.Current.CancellationToken);

        lock (traffic)
        {
            Assert.Equal(
                [
                    KissTrafficKind.DamagedNmea,
                    KissTrafficKind.DamagedNmea,
                    KissTrafficKind.DamagedNmea,
                    KissTrafficKind.Nmea,
                    KissTrafficKind.Frame,
                ],
                traffic.Select(e => e.Kind)
            );
            Assert.All(traffic.Take(4), e => Assert.True(e.EndsLine));
        }

        lock (sentences)
        {
            Assert.Equal([waypoint], sentences.Select(e => e.Sentence.Text));
        }
    }

    [Fact]
    public async Task TheGpsIsThereFromTheFirstSentenceUntilNoneComesForTheTimeout()
    {
        var clock = new FakeTimeProvider();
        var tnc = new FakeTnc();
        await using var connection = Connection(tnc, clock);
        var changes = Record<GpsPresenceChangedEventArgs>(h => connection.GpsPresenceChanged += h);
        var sentences = Record<NmeaSentenceEventArgs>(h => connection.NmeaSentenceReceived += h);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var packets = connection.ReadPacketsAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        var reading = packets.MoveNextAsync().AsTask();

        async Task SendSentence()
        {
            var before = Count(sentences);
            tnc.Send(Text($"{Gga}\r\n"));
            await WaitFor(() => Count(sentences) > before);
        }

        Assert.Equal(TimeSpan.FromSeconds(10), connection.GpsTimeout);
        Assert.False(connection.IsGpsPresent);
        await SendSentence();
        Assert.True(connection.IsGpsPresent);
        await SendSentence(); // the GPS was there already: no change
        Assert.Equal([true], Presence());

        clock.Advance(TimeSpan.FromSeconds(9));
        await SendSentence(); // just in time
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal([true], Presence());
        Assert.True(connection.IsGpsPresent);

        clock.Advance(TimeSpan.FromSeconds(1)); // ten seconds since the last
        Assert.Equal([true, false], Presence());
        Assert.False(connection.IsGpsPresent);
        Assert.All(changes, e => Assert.Equal(TimeSpan.FromSeconds(10), e.Timeout));

        await SendSentence();
        Assert.Equal([true, false, true], Presence());
        Assert.False(reading.IsCompleted); // sentences are not packets
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);

        bool[] Presence()
        {
            lock (changes)
            {
                return [.. changes.Select(e => e.IsPresent)];
            }
        }
    }

    [Fact]
    public async Task AHandlerThatThrowsDoesNotStopReading()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(tnc);
        connection.NmeaSentenceReceived += (_, _) => throw new InvalidOperationException("an app's bug");
        connection.GpsPresenceChanged += (_, _) => throw new InvalidOperationException("an app's bug");
        tnc.Send(Text($"{Gga}\r\nW1AW>APRS:>still read\r\n"));

        var packets = await ReadUntilEnd(connection, 1).WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal("W1AW>APRS:>still read", packets.Single().ToString());
    }

    [Fact]
    public void TheGpsTimeoutMustBePositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KissSerialOptions
            {
                PortName = "/dev/ttyUSB0",
                TextMode = true,
                GpsTimeout = TimeSpan.Zero,
            }.Validate()
        );

    private static int Count<T>(List<T> list)
    {
        lock (list)
        {
            return list.Count;
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Five minutes recorded from a TH-D72A in APRS mode with its GPS output on (2026-10-20), each GPS position and
    /// altitude replaced with APRS101's example, fed in as a serial port reads it. One packet ran into the middle of a
    /// satellite sentence: that line starts as a sentence, so it is a damaged sentence and gives no packet. The radio
    /// had no fix for its first minute and a half, and its placeholder position meanwhile is never read as one.
    /// </summary>
    [Fact]
    public async Task ARecordingFromATHD72AGivesItsPacketsAndItsGpsSentences()
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
        var traffic = Record<KissTrafficEventArgs>(h => connection.Traffic += h);
        var sentences = Record<NmeaSentenceEventArgs>(h => connection.NmeaSentenceReceived += h);
        var presence = Record<GpsPresenceChangedEventArgs>(h => connection.GpsPresenceChanged += h);
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
            Assert.Equal(1358, traffic.Count(e => e.Kind == KissTrafficKind.Nmea));
            Assert.StartsWith(
                "$GPGSV,3,2,12,07,33,075,,05,30,266W0MB-9>",
                traffic.Single(e => e.Kind == KissTrafficKind.DamagedNmea).Text,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(traffic, e => e.Kind == KissTrafficKind.Unframed);
        }

        lock (sentences)
        {
            Assert.Equal(1358, sentences.Count);
            var tracker = new NmeaFixTracker();
            var first = sentences.FindIndex(e => e.Sentence is { Type: "GGA", Fields: [_, _, _, _, _, not "0", ..] });
            Assert.Equal(377, first); // the damaged line before it is no sentence
            foreach (var sentence in sentences.Take(first))
            {
                // GGA quality 0, RMC and GLL V, GSA mode 1: no fix, and the radio's placeholder position isn't one.
                tracker.Update(sentence.Sentence);
                Assert.Null(tracker.Fix.Position);
            }

            tracker.Update(sentences[first].Sentence);
            Assert.Equal(GpsFixMode.Fix, tracker.Fix.Mode);
            Assert.Equal(49.0583, tracker.Fix.Position!.Value.Latitude, 4);
        }

        lock (presence)
        {
            Assert.Equal([true], presence.Select(e => e.IsPresent));
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
