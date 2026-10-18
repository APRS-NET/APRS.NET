using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

public sealed class TncScriptTests
{
    private readonly FakeTimeProvider _time = new();

    private static byte[] Text(string text) => Encoding.Latin1.GetBytes(text);

    /// <summary>A TNC at its cmd: prompt: echoes each command and prompts again.</summary>
    private static FakeTnc CommandLine() =>
        new() { Answer = written => Text($"{Encoding.Latin1.GetString(written).TrimEnd('\r')}\r\ncmd:") };

    /// <summary>Moves the fake clock until <paramref name="task"/> ends, so pauses and timeouts pass.</summary>
    private async Task<T> Pump<T>(Task<T> task)
    {
        for (var i = 0; i < 1000 && !task.IsCompleted; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        return await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void TheTextFormReadsAndWritesEachStep()
    {
        var script = TncScript.Parse(
            """
            # A comment, and a blank line

            bytes C0 FF C0
            send
            expect 3s cmd:
            SEND KISS ON
            pause 500ms
            expect 1.5s Your call: 
            """
        );

        Assert.Equal(
            [
                TncScriptStepKind.Send,
                TncScriptStepKind.Send,
                TncScriptStepKind.Expect,
                TncScriptStepKind.Send,
                TncScriptStepKind.Pause,
                TncScriptStepKind.Expect,
            ],
            script.Steps.Select(s => s.Kind)
        );
        Assert.Equal(new byte[] { 0xC0, 0xFF, 0xC0 }, script.Steps[0].Bytes.ToArray());
        Assert.Equal("\r"u8.ToArray(), script.Steps[1].Bytes.ToArray());
        Assert.Equal(("cmd:", TimeSpan.FromSeconds(3)), (script.Steps[2].Text, script.Steps[2].Time));
        Assert.Equal("KISS ON\r"u8.ToArray(), script.Steps[3].Bytes.ToArray());
        Assert.Equal(TimeSpan.FromMilliseconds(500), script.Steps[4].Time);
        Assert.Equal("Your call:", script.Steps[5].Text);
        Assert.Equal(
            "bytes C0FFC0\nsend \nexpect 3s cmd:\nsend KISS ON\npause 0.5s\nexpect 1.5s Your call:",
            script.ToString()
        );
        Assert.Equal(script.ToString(), TncScript.Parse(script.ToString()).ToString());
        Assert.Equal("\xC0\xFF\xC0\rKISS ON\r", Encoding.Latin1.GetString(script.SentBytes().Span));
    }

    [Theory]
    [InlineData("send\nreset", "Line 2", "isn't a script step")]
    [InlineData("pause 5", "Line 1", "isn't a time")]
    [InlineData("pause soon", "Line 1", "isn't a time")]
    [InlineData("expect 3s", "Line 1", "needs a time and some text")]
    [InlineData("bytes C0 F", "Line 1", "isn't hex bytes")]
    [InlineData("bytes", "Line 1", "needs hex bytes")]
    public void AStepThatCannotBeReadNamesItsLine(string text, string line, string why)
    {
        var ex = Assert.Throws<FormatException>(() => TncScript.Parse(text));

        Assert.StartsWith(line, ex.Message, StringComparison.Ordinal);
        Assert.Contains(why, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunSendsEachCommandAndWaitsForThePrompt()
    {
        var tnc = CommandLine();
        var script = TncScript.Parse("send\nexpect 3s CMD:\nsend KISS ON\nexpect 3s cmd:\nsend RESTART");

        var transcript = await Pump(script.RunAsync(tnc, _time, TestContext.Current.CancellationToken));

        Assert.Equal("\rKISS ON\rRESTART\r", Encoding.Latin1.GetString(tnc.Written.ToArray()));
        Assert.Equal("\r\ncmd:KISS ON\r\ncmd:", transcript); // the reply to RESTART wasn't waited for
    }

    [Fact]
    public async Task AnExpectedTextCanArriveInPiecesButOnlyNewTextCounts()
    {
        var tnc = new FakeTnc();
        tnc.Send(Text("cm"));
        tnc.Send(Text("d:"));
        var script = TncScript.Parse("expect 3s cmd:\nexpect 3s cmd:");

        var ex = await Assert.ThrowsAsync<TncScriptException>(() =>
            Pump(script.RunAsync(tnc, _time, TestContext.Current.CancellationToken))
        );

        // The first prompt matched the first expect; the second needed another one, which never came.
        Assert.Same(script.Steps[1], ex.Step);
        Assert.Equal("cmd:", ex.Transcript);
        Assert.Equal("The TNC didn't send 'cmd:' within 3s.", ex.Message);
        Assert.IsAssignableFrom<IOException>(ex); // a lost link to a connection, which retries
    }

    [Fact]
    public async Task APortThatClosesWhileWaitingFailsAtOnce()
    {
        var tnc = new FakeTnc();
        tnc.End();

        var ex = await Assert.ThrowsAsync<TncScriptException>(() =>
            TncScript.Parse("expect 10s cmd:").RunAsync(tnc, _time, TestContext.Current.CancellationToken)
        );

        Assert.Contains("closed the port", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StepsRefuseWhatCannotBeSent()
    {
        Assert.Throws<ArgumentException>(() => TncScriptStep.SendLine("€"));
        Assert.Throws<ArgumentException>(() => TncScriptStep.Expect("", TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TncScriptStep.Expect("cmd:", TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => TncScriptStep.Pause(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task TheConsoleSendsCommandsAndReadsWhatTheTncSays()
    {
        var tnc = CommandLine();
        await using var terminal = new TncTerminal(tnc);

        await terminal.SendLineAsync("MYCALL", TestContext.Current.CancellationToken);
        await terminal.SendAsync(TncSetup.KissExitFrame, TestContext.Current.CancellationToken);
        tnc.End();

        var said = new StringBuilder();
        await foreach (var piece in terminal.ReadAsync(TestContext.Current.CancellationToken))
        {
            said.Append(piece);
        }

        Assert.Equal("MYCALL\r\xC0\xFF\xC0", Encoding.Latin1.GetString(tnc.Written.ToArray()));
        Assert.StartsWith("MYCALL\r\ncmd:", said.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheConsoleCanRunAScriptAndClosesThePortUnlessToldNotTo()
    {
        var tnc = CommandLine();
        await using (var terminal = new TncTerminal(tnc))
        {
            var transcript = await Pump(
                terminal.RunAsync(TncScript.Parse("send\nexpect 1s cmd:"), _time, TestContext.Current.CancellationToken)
            );
            Assert.Equal("\r\ncmd:", transcript);
        }

        Assert.True(tnc.Disposed);

        var kept = new FakeTnc();
        await new TncTerminal(kept, ownsStream: false).DisposeAsync();
        Assert.False(kept.Disposed);
    }
}
