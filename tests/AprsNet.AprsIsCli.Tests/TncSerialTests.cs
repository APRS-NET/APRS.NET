namespace AprsNet.AprsIsCli.Tests;

/// <summary>
/// <c>--tnc serial:PORT</c> without a TNC: parsing, the port list, and a port that doesn't exist. A real USB TNC is
/// checked by hand.
/// </summary>
public sealed class TncSerialTests
{
    private static readonly string Missing = OperatingSystem.IsWindows() ? "COM250" : "/dev/aprsnet-no-such-port";

    [Theory]
    [InlineData("serial:/dev/ttyUSB0", "/dev/ttyUSB0", 9600)]
    [InlineData("SERIAL:COM3@57600", "COM3", 57600)]
    [InlineData(
        " serial:/dev/serial/by-id/usb-FTDI_TNC-X-if00-port0@1200 ",
        "/dev/serial/by-id/usb-FTDI_TNC-X-if00-port0",
        1200
    )]
    public void ASerialTncIsAPortAndABaudRate(string text, string port, int baud)
    {
        Assert.True(TncAddress.TryParse(text, out var address));
        Assert.True(address.IsSerial);
        Assert.Equal(port, address.SerialPort);
        Assert.Equal(baud, address.BaudRate);
        Assert.Equal(port, address.ToString());
    }

    [Theory]
    [InlineData("localhost", "localhost:8001")]
    [InlineData("127.0.0.1:8002", "127.0.0.1:8002")]
    public void ATcpTncIsAHostAndPort(string text, string shown)
    {
        Assert.True(TncAddress.TryParse(text, out var address));
        Assert.False(address.IsSerial);
        Assert.Equal(shown, address.ToString());
    }

    [Theory]
    [InlineData("serial:")]
    [InlineData("serial:@9600")]
    [InlineData("serial:COM3@")]
    [InlineData("serial:COM3@fast")]
    [InlineData("serial:COM3@0")]
    public void AnUnusableSerialTncIsRefused(string text) => Assert.False(TncAddress.TryParse(text, out _));

    [Fact]
    public async Task ListenRefusesAnUnusableSerialTnc()
    {
        var result = await new CliHarness().RunAsync(
            ["tnc", "listen", "--tnc", "serial:COM3@fast"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Use host[:port] or serial:PORT[@BAUD]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListenWithoutTheSerialPortIsAConnectionFailure()
    {
        var result = await new CliHarness().RunAsync(
            ["tnc", "listen", "--tnc", $"serial:{Missing}"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"could not connect to the TNC at {Missing}", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithReconnectListeningWaitsForTheSerialPort()
    {
        var result = await new CliHarness().RunAsync(
            ["tnc", "listen", "--tnc", $"serial:{Missing}", "--reconnect", "--duration", "1s"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"lost the TNC at {Missing}", result.Error, StringComparison.Ordinal);
        Assert.Contains("reconnecting in 2s.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendingWithoutTheSerialPortIsAConnectionFailure()
    {
        var result = await new CliHarness().RunAsync(
            ["tnc", "send", "status", "Testing", "-c", "KD9XYZ-9", "--tnc", $"serial:{Missing}"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"could not send through the TNC at {Missing}", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServeWaitsForASerialTnc()
    {
        var result = await new CliHarness().RunAsync(
            ["serve", "--tnc", $"serial:{Missing}", "--listen", "127.0.0.1:0", "--duration", "1s"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"serving the TNC at {Missing}", result.Error, StringComparison.Ordinal);
        Assert.Contains($"lost the TNC at {Missing}", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PortsListsTheSerialPorts()
    {
        var result = await new CliHarness { SerialPorts = ["/dev/ttyACM0", "/dev/ttyUSB0"] }.RunAsync(
            ["tnc", "ports"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"/dev/ttyACM0{Environment.NewLine}/dev/ttyUSB0{Environment.NewLine}", result.Out);
    }

    [Fact]
    public async Task PortsSaysWhenThereAreNone()
    {
        var result = await new CliHarness().RunAsync(["tnc", "ports"], TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Out);
        Assert.Contains("no serial ports found", result.Error, StringComparison.Ordinal);
    }
}
