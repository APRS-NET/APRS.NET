namespace AprsNet.AprsIsCli.Tests;

public class PasscodeTests
{
    [Theory]
    [InlineData("N0CALL")]
    [InlineData("n0call-9")]
    [InlineData(" N0CALL ")]
    public async Task PrintsOnlyThePasscode(string callsign)
    {
        var result = await new CliHarness().RunAsync(["passcode", callsign], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"13023{Environment.NewLine}", result.Out);
        Assert.Equal("", result.Error);
    }

    [Theory]
    [InlineData("N0/CALL")]
    [InlineData("N0CALLTOOLONG")]
    [InlineData("-9")]
    public async Task RejectsAnInvalidCallsignAsAUsageError(string callsign)
    {
        var result = await new CliHarness().RunAsync(["passcode", callsign], TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("", result.Out);
        Assert.Contains("callsign must be 1 to 9 letters or digits", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequiresACallsign()
    {
        var result = await new CliHarness().RunAsync(["passcode"], TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("", result.Out);
    }

    [Fact]
    public async Task DoesNotConnect()
    {
        var harness = new CliHarness();

        await harness.RunAsync(["passcode", "N0CALL"], TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.ConnectionAttempts);
    }
}
