using System.Text;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

/// <summary>A radio's built-in TNC switched into KISS by its <see cref="TncSetup"/> each time the port opens.</summary>
public sealed class TncSetupTests
{
    private const string EnterBytes = "\xC0\xFF\xC0\rKISS ON\rRESTART\r";
    private readonly FakeTimeProvider _time = new();

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)), 0).Encode();

    private static readonly byte[] TncText = Encoding.Latin1.GetBytes(
        string.Concat(Enumerable.Repeat("N0CALL-9>APRS,WIDE1-1:!4903.50N/07201.75W-Packet mode text, not KISS\r\n", 3))
    );

    /// <summary>
    /// A TH-D72A in packet mode, answering as the real one did on 17 October 2026 (TNC firmware 2.0): it echoes each
    /// command, answers the KISS exit frame at its prompt with ?EH, KISS ON with "KISS     was OFF", and restarts into
    /// KISS silently, sending only frames after that.
    /// </summary>
    private static FakeTnc Radio(params string[] heard) => Radio(heard, afterRestart: null);

    private static FakeTnc Radio(string[] heard, byte[]? afterRestart)
    {
        var radio = new FakeTnc();
        var badCommand = false;
        radio.Answer = written =>
        {
            var text = Encoding.Latin1.GetString(written);
            switch (text)
            {
                case "\xC0\xFF\xC0":
                    badCommand = true;
                    return written; // echoed, as typed
                case "\r":
                    var answer = badCommand ? "\r\n?EH\r\ncmd:" : "\r\ncmd:";
                    badCommand = false;
                    return Encoding.Latin1.GetBytes(answer);
                case "KISS ON\r":
                    return Encoding.Latin1.GetBytes("KISS ON\r\nKISS     was OFF\r\ncmd:");
                case "RESTART\r":
                    radio.Send("R"u8.ToArray());
                    if (afterRestart is not null)
                    {
                        radio.Send(afterRestart);
                    }

                    foreach (var line in heard)
                    {
                        radio.Send(Kiss(line));
                    }

                    return null;
                default:
                    return null;
            }
        };
        return radio;
    }

    private KissSerialConnection Connection(Func<CancellationToken, Task<Stream>> open) =>
        new(
            open,
            new KissSerialOptions
            {
                PortName = "/dev/ttyUSB0",
                TimeProvider = _time,
                Setup = TncSetup.KenwoodThD72,
            }
        );

    /// <summary>Moves the fake clock until <paramref name="task"/> ends, so the setup's pauses pass.</summary>
    private async Task<T> Pump<T>(Task<T> task)
    {
        for (var i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        return await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheRadioIsSwitchedIntoKissWhenThePortOpens()
    {
        var radio = Radio("N0CALL>APRS:>heard in KISS");
        await using var connection = Connection(_ => Task.FromResult<Stream>(radio));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await Pump(packets.MoveNextAsync().AsTask()));

        Assert.Equal("N0CALL>APRS:>heard in KISS", packets.Current.ToString());
        Assert.Equal(EnterBytes, Encoding.Latin1.GetString(radio.Written.ToArray()));
    }

    [Fact]
    public async Task ARadioThatDoesNotAnswerIsRetriedAndSaysWhatToDo()
    {
        var silent = new FakeTnc(); // in APRS mode it never shows the cmd: prompt
        var radio = Radio("N0CALL>APRS:>after switching to packet mode");
        var attempts = 0;
        await using var connection = Connection(_ => Task.FromResult<Stream>(++attempts == 1 ? silent : radio));
        var errors = new List<Exception?>();
        connection.StateChanged += (_, e) =>
        {
            if (e.State == KissConnectionState.Reconnecting)
            {
                lock (errors)
                {
                    errors.Add(e.Error);
                }
            }
        };
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await Pump(packets.MoveNextAsync().AsTask()));

        Assert.Equal(2, attempts);
        Assert.True(silent.Disposed);
        var error = Assert.IsType<IOException>(Assert.Single(errors));
        Assert.Contains("didn't switch to KISS mode (Kenwood TH-D72)", error.Message, StringComparison.Ordinal);
        Assert.Contains("PACKET12", error.Message, StringComparison.Ordinal);
        Assert.IsType<TncScriptException>(error.InnerException);
    }

    [Fact]
    public async Task ARadioSendingTextAfterAPowerCutIsSwitchedBackIntoKiss()
    {
        // The first time it is switched into KISS, then loses power and comes back sending text: the port opens again.
        var first = Radio([], afterRestart: TncText);
        var second = Radio("N0CALL>APRS:>back in KISS");
        var attempts = 0;
        await using var connection = Connection(_ => Task.FromResult<Stream>(++attempts == 1 ? first : second));
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

        Assert.Equal("N0CALL>APRS:>back in KISS", packets.Current.ToString());
        Assert.Equal(2, attempts);
        Assert.Equal(EnterBytes, Encoding.Latin1.GetString(second.Written.ToArray()));
        lock (warnings)
        {
            Assert.Equal([true, false], warnings);
        }
    }

    [Fact]
    public void ThePortSaysGoodbyeBeforeItClosesHoweverItCloses()
    {
        var tnc = new FakeTnc();
        var port = new FakePort();
        var stream = new KissSerialConnection.SerialPortStream(tnc, port)
        {
            Goodbye = TncSetup.KenwoodThD72.LeaveKiss!.SentBytes(),
        };

        stream.Dispose();
        stream.Dispose();

        Assert.Equal(new byte[] { 0xC0, 0xFF, 0xC0 }, tnc.Written.ToArray()); // once, before the port closed
        Assert.True(port.Closed);
    }

    [Fact]
    public async Task CancellingAReadAlsoSaysGoodbye()
    {
        var tnc = new FakeTnc();
        var port = new FakePort();
        await using var stream = new KissSerialConnection.SerialPortStream(tnc, port)
        {
            Goodbye = TncSetup.KissExitFrame,
        };
        using var cancel = new CancellationTokenSource();

        var read = stream.ReadAsync(new byte[16], cancel.Token).AsTask();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(new byte[] { 0xC0, 0xFF, 0xC0 }, tnc.Written.ToArray());
        Assert.True(port.Closed);
    }

    [Fact]
    public void BuiltInSetupsAreFoundByTheirId()
    {
        Assert.Same(TncSetup.KenwoodThD72, TncSetup.Find("TH-D72"));
        Assert.Null(TncSetup.Find("tnc-x"));
        Assert.Contains(TncSetup.KenwoodThD72, TncSetup.Known);
    }

    private sealed class FakePort : IDisposable
    {
        public bool Closed { get; private set; }

        public void Dispose() => Closed = true;
    }
}
