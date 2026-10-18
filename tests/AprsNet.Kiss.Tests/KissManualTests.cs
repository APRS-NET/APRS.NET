using System.Text;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

/// <summary>Working at a TNC's command line by hand over an open connection, for a troubleshooting console.</summary>
public sealed class KissManualTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _time = new();

    private static byte[] Text(string text) => Encoding.Latin1.GetBytes(text);

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)), 0).Encode();

    private static readonly byte[] TncText = Text(
        string.Concat(Enumerable.Repeat("N0CALL-9>APRS,WIDE1-1:!4903.50N/07201.75W-printed as text\r\n", 3))
    );

    /// <summary>A TH-D72 at its cmd: prompt, answering as the recorded one did; RESTART puts it in KISS.</summary>
    private static FakeTnc Radio(string heardAfterRestart)
    {
        var radio = new FakeTnc();
        radio.Answer = written =>
            Encoding.Latin1.GetString(written) switch
            {
                "\xC0\xFF\xC0" => written,
                "\r" => Text("\r\n?EH\r\ncmd:"),
                "KISS ON\r" => Text("KISS ON\r\nKISS     was OFF\r\ncmd:"),
                "RESTART\r" => [.. "R"u8, .. Kiss(heardAfterRestart)],
                _ => null,
            };
        return radio;
    }

    private KissSerialConnection Connection(Func<CancellationToken, Task<Stream>> open, TncSetup? setup = null) =>
        new(
            open,
            new KissSerialOptions
            {
                PortName = "/dev/ttyUSB0",
                TimeProvider = _time,
                Setup = setup,
            }
        );

    private static List<KissTrafficEventArgs> Record(KissConnection connection)
    {
        var entries = new List<KissTrafficEventArgs>();
        connection.Traffic += (_, e) =>
        {
            lock (entries)
            {
                entries.Add(e);
            }
        };
        return entries;
    }

    private static List<KissTrafficEventArgs> Snapshot(List<KissTrafficEventArgs> entries)
    {
        lock (entries)
        {
            return [.. entries];
        }
    }

    /// <summary>Moves the fake clock until <paramref name="task"/> ends, so a script's pauses pass.</summary>
    private async Task<T> Pump<T>(Task<T> task)
    {
        for (var i = 0; i < 1000 && !task.IsCompleted; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        return await task.WaitAsync(Wait, TestContext.Current.CancellationToken);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Wait);
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    [Fact]
    public async Task SendingByHandNeedsManualModeAndAConnection()
    {
        await using var connection = Connection(_ => Task.FromResult<Stream>(new FakeTnc()));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.SendLineAsync("KISS ON", TestContext.Current.CancellationToken)
        );
        Assert.Contains("BeginManual", refused.Message, StringComparison.Ordinal);

        using (connection.BeginManual())
        using (connection.BeginManual())
        {
            Assert.True(connection.IsManual);
            var notConnected = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                connection.SendLineAsync("KISS ON", TestContext.Current.CancellationToken)
            );
            Assert.Contains("not connected", notConnected.Message, StringComparison.Ordinal);
        }

        Assert.False(connection.IsManual);
    }

    [Fact]
    public async Task ALineTypedByHandGoesOutBetweenFramesAndItsAnswerComesBackAsText()
    {
        // Echoes a typed line and prompts again; a frame it transmits, without a word.
        var tnc = new FakeTnc
        {
            Answer = written =>
                written[0] == KissFrame.FrameEnd
                    ? null
                    : Text($"{Encoding.Latin1.GetString(written).TrimEnd('\r')}\r\ncmd:"),
        };
        await using var connection = Connection(_ => Task.FromResult<Stream>(tnc));
        var entries = Record(connection);
        tnc.Send(Kiss("N0CALL>APRS:>first"));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await packets.MoveNextAsync());
        var reading = packets.MoveNextAsync().AsTask(); // a console always has a read waiting
        using var manual = connection.BeginManual();

        // A frame and a typed line at once: each is written whole, one after the other.
        await Task.WhenAll(
            connection
                .SendAsync(AprsPacket.Parse("N0CALL>APZNET:>a frame"), TestContext.Current.CancellationToken)
                .AsTask(),
            connection.SendLineAsync("MYCALL", TestContext.Current.CancellationToken)
        );
        var frame = Kiss("N0CALL>APZNET:>a frame");
        var written = tnc.Written.ToArray();
        Assert.True(
            written.SequenceEqual([.. frame, .. "MYCALL\r"u8]) || written.SequenceEqual([.. "MYCALL\r"u8, .. frame]),
            Encoding.Latin1.GetString(written)
        );

        await WaitUntil(() =>
            string.Concat(Snapshot(entries).Where(e => e.Kind == KissTrafficKind.Unframed).Select(e => e.Text))
                .Contains("MYCALL\r\ncmd:", StringComparison.Ordinal)
        );
        var seen = Snapshot(entries);
        Assert.Contains(seen, e => e is { Kind: KissTrafficKind.RawSent, Text: "MYCALL\r" });
        // The prompt and the next answer may come in one read, so check the text as the console shows it.
        Assert.Contains(
            "MYCALL\r\ncmd:",
            string.Concat(seen.Where(e => e.Kind == KissTrafficKind.Unframed).Select(e => e.Text)),
            StringComparison.Ordinal
        );

        await connection.DisposeAsync();
        Assert.False(await reading.WaitAsync(Wait, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AScriptSwitchesTheTncIntoKissWithoutReopeningThePort()
    {
        var radio = Radio(heardAfterRestart: "N0CALL>APRS:>in KISS on the same port");
        var opened = 0;
        await using var connection = Connection(_ =>
        {
            opened++;
            return Task.FromResult<Stream>(radio);
        });
        var entries = Record(connection);
        radio.Send(TncText); // a radio left in packet mode with KISS off: text, no frames
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = packets.MoveNextAsync().AsTask();
        await WaitUntil(() => connection.IsConnected && connection.IsSendingText);

        using (connection.BeginManual())
        {
            var transcript = await Pump(
                connection.RunScriptAsync(TncSetup.KenwoodThD72.EnterKiss, TestContext.Current.CancellationToken)
            );
            Assert.Contains("KISS     was OFF", transcript, StringComparison.Ordinal);
        }

        Assert.True(await next.WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.Equal("N0CALL>APRS:>in KISS on the same port", packets.Current.ToString());
        Assert.Equal(1, opened);
        Assert.Equal(
            "\xC0\xFF\xC0\rKISS ON\rRESTART\r",
            string.Concat(Snapshot(entries).Where(e => e.Kind == KissTrafficKind.SetupSent).Select(e => e.Text))
        );
        Assert.DoesNotContain(Snapshot(entries), e => e.Kind == KissTrafficKind.SetupReceived); // that's Unframed
    }

    [Fact]
    public async Task OneScriptAtATime()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(_ => Task.FromResult<Stream>(tnc));
        tnc.Send(Kiss("N0CALL>APRS:>first"));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await packets.MoveNextAsync());
        var reading = packets.MoveNextAsync().AsTask();
        using var manual = connection.BeginManual();

        var waiting = connection.RunScriptAsync(
            TncScript.Parse("expect 5s never"),
            TestContext.Current.CancellationToken
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.RunScriptAsync(TncScript.Parse("send"), TestContext.Current.CancellationToken)
        );

        await Assert.ThrowsAsync<TncScriptException>(() => Pump(waiting));
        await connection.DisposeAsync();
        Assert.False(await reading.WaitAsync(Wait, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InManualModeTextDoesNotMakeASetupReopenThePort()
    {
        // The setup switched the radio into KISS; then someone typed the KISS exit frame by hand, and it prints text.
        var radio = Radio(heardAfterRestart: "N0CALL>APRS:>in KISS");
        var opened = 0;
        await using var connection = Connection(
            _ =>
            {
                opened++;
                return Task.FromResult<Stream>(radio);
            },
            TncSetup.KenwoodThD72
        );
        var warnings = new List<bool>();
        connection.Warning += (_, e) =>
        {
            lock (warnings)
            {
                warnings.Add(e.IsActive);
            }
        };
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await Pump(packets.MoveNextAsync().AsTask()));

        using var manual = connection.BeginManual();
        var next = packets.MoveNextAsync().AsTask();
        radio.Send(TncText);
        await WaitUntil(() => connection.IsSendingText);
        _time.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(1, opened);
        Assert.Equal(KissConnectionState.Connected, connection.State);
        Assert.False(next.IsCompleted);
        lock (warnings)
        {
            Assert.Equal([true], warnings);
        }

        await connection.DisposeAsync(); // ends the read still waiting, before the stream is disposed
        Assert.False(await next.WaitAsync(Wait, TestContext.Current.CancellationToken));
    }
}
