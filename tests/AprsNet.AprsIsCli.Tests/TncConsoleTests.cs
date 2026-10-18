namespace AprsNet.AprsIsCli.Tests;

/// <summary><c>tnc kiss on|off</c> and <c>tnc terminal</c>, against a fake TNC command line.</summary>
public sealed class TncConsoleTests
{
    [Theory]
    [InlineData("serial:/dev/ttyUSB0,setup=th-d72", "th-d72")]
    [InlineData("serial:/dev/ttyUSB0@9600,dtr=off,SETUP=TH-D72", "th-d72")]
    [InlineData("serial:/dev/ttyUSB0", null)]
    public void ASerialAddressCanNameTheTncsSetup(string text, string? setup)
    {
        Assert.True(TncAddress.TryParse(text, out var address));
        Assert.Equal(setup, address.Setup?.Id);
        Assert.Equal("/dev/ttyUSB0", address.ToString());
    }

    [Fact]
    public void AnUnknownSetupIsRefused() =>
        Assert.False(TncAddress.TryParse("serial:/dev/ttyUSB0,setup=tnc-x", out _));

    [Fact]
    public async Task KissOnRunsTheRadiosSetupOnce()
    {
        var tnc = new FakeCommandLine();
        var harness = new CliHarness { TncPort = tnc };

        var result = await harness.RunAsync(
            ["tnc", "kiss", "on", "-t", "serial:/dev/ttyUSB0,setup=th-d72"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "The TNC at /dev/ttyUSB0 is in KISS mode (Kenwood TH-D72).\n",
            result.Out.ReplaceLineEndings("\n")
        );
        Assert.Equal("\xC0\xFF\xC0\rKISS ON\rRESTART\r", tnc.WrittenText);
    }

    [Fact]
    public async Task KissOffSendsTheExitFrame()
    {
        var tnc = new FakeCommandLine();
        var harness = new CliHarness { TncPort = tnc };

        var result = await harness.RunAsync(
            ["tnc", "kiss", "off", "-t", "serial:/dev/ttyUSB0,setup=th-d72"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("has left KISS mode", result.Out, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 0xC0, 0xFF, 0xC0 }, tnc.Written);
    }

    [Fact]
    public async Task ARadioThatDoesNotAnswerSaysWhatToDo()
    {
        var harness = new CliHarness { TncPort = new FakeCommandLine(silent: true) };

        var result = await harness.RunAsync(
            ["tnc", "kiss", "on", "-t", "serial:/dev/ttyUSB0,setup=th-d72"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("didn't switch to KISS mode", result.Error, StringComparison.Ordinal);
        Assert.Contains("PACKET12", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("serial:/dev/ttyUSB0", "needs the TNC's setup")]
    [InlineData("localhost:8001", "on a serial port")]
    [InlineData("serial:/dev/ttyUSB0,setup=nope", "is not valid")]
    public async Task KissNeedsASerialTncAndItsSetup(string tnc, string why)
    {
        var harness = new CliHarness { TncPort = new FakeCommandLine() };

        var result = await harness.RunAsync(["tnc", "kiss", "on", "-t", tnc], TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(why, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingPortCannotBeOpened()
    {
        var result = await new CliHarness().RunAsync(
            ["tnc", "terminal", "-t", "serial:/dev/ttyUSB9"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("cannot open the TNC at /dev/ttyUSB9", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTerminalSendsTypedCommandsAndScriptStepsAndPrintsTheAnswers()
    {
        var tnc = new FakeCommandLine();
        var harness = new CliHarness { TncPort = tnc, Input = "MYCALL\n~bytes C0 FF C0\n\n~reset\n" };

        var result = await harness.RunAsync(
            ["tnc", "terminal", "-t", "serial:/dev/ttyUSB0@9600,rts=off"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("MYCALL\r\xC0\xFF\xC0\r", tnc.WrittenText);
        Assert.Equal("MYCALL\ncmd:\\xC0\\xFF\\xC0\n?EH\ncmd:", result.Out);
        Assert.Contains("isn't a script step", result.Error, StringComparison.Ordinal); // ~reset
        Assert.False(harness.OpenedTnc!.Rts);
    }

    [Fact]
    public async Task TheTerminalCanRunAScriptFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aprscli-script-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "send\nexpect 3s cmd:\nsend VERSION", TestContext.Current.CancellationToken);
        try
        {
            var tnc = new FakeCommandLine();
            var harness = new CliHarness { TncPort = tnc };

            var result = await harness.RunAsync(
                ["tnc", "terminal", "-t", "serial:/dev/ttyUSB0", "--script", path],
                TestContext.Current.CancellationToken
            );

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("\rVERSION\r", tnc.WrittenText);
            Assert.Equal("\ncmd:\n", result.Out.ReplaceLineEndings("\n"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("cmd:\r\n", false, "cmd:\n", false)]
    [InlineData("a\rb\nc", false, "a\nb\nc", false)]
    [InlineData("\n", true, "", false)] // the LF of a CR LF split across reads
    [InlineData("x\r", false, "x\n", true)]
    [InlineData("\u001B[2JÀ", false, "\\x1B[2J\\xC0", false)]
    public void TheTerminalPrintsLineBreaksOnceAndControlBytesAsEscapes(
        string text,
        bool afterCr,
        string printed,
        bool endsWithCr
    )
    {
        Assert.Equal(printed, TncConsoleCommand.Printable(text, out var ends, afterCr));
        Assert.Equal(endsWithCr, ends);
    }
}
