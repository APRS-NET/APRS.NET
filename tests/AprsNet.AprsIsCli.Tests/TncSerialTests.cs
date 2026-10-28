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
    [InlineData("serial:/dev/ttyUSB0", true, true)]
    [InlineData("serial:/dev/ttyUSB0@115200,dtr=off,rts=off", false, false)]
    [InlineData("serial:COM5,RTS=off", true, false)]
    [InlineData("serial:COM5, dtr=off , rts=on", false, true)]
    public void ASerialTncCanLeaveDtrAndRtsLow(string text, bool dtr, bool rts)
    {
        Assert.True(TncAddress.TryParse(text, out var address));
        Assert.Equal(dtr, address.Dtr);
        Assert.Equal(rts, address.Rts);
        Assert.DoesNotContain(",", address.ToString(), StringComparison.Ordinal);
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
    [InlineData("serial:COM3,dtr")]
    [InlineData("serial:COM3,dtr=low")]
    [InlineData("serial:COM3,cts=off")]
    [InlineData("serial:,dtr=off")]
    public void AnUnusableSerialTncIsRefused(string text) => Assert.False(TncAddress.TryParse(text, out _));

    [Fact]
    public async Task ListenRefusesAnUnusableSerialTnc()
    {
        var result = await new CliHarness().RunAsync(
            ["tnc", "listen", "--tnc", "serial:COM3@fast"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(
            "Use host[:port], agw:host[:port] or serial:PORT[@BAUD]",
            result.Error,
            StringComparison.Ordinal
        );
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
        if (OperatingSystem.IsLinux())
        {
            // .NET says "access denied" for a device that isn't there; the message says what's wrong.
            Assert.Contains(
                $"{Missing} is not present; is the TNC plugged in?",
                result.Error,
                StringComparison.Ordinal
            );
        }
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

    // A radio in its APRS mode with GPS PC output on: a GPS sentence, then a packet line. Made up.
    private static MemoryStream RadioInAprsMode() =>
        new(
            System.Text.Encoding.Latin1.GetBytes(
                "$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,*47\r\nW1AW>APRS:>heard\r\n"
            )
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListeningInTextModeSaysWhenTheRadioSendsGpsSentences(bool reconnect)
    {
        var harness = new CliHarness { TncPort = RadioInAprsMode() };
        string[] args = ["tnc", "listen", "--tnc", "serial:/dev/ttyUSB0,text", "-n", "1"];

        var result = await harness.RunAsync(
            reconnect ? [.. args, "--reconnect"] : args,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("W1AW>APRS:>heard", result.Out, StringComparison.Ordinal);
        Assert.Contains(
            "aprscli: the TNC at /dev/ttyUSB0 is sending GPS sentences (NMEA).",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("$GPGGA", result.Out, StringComparison.Ordinal);
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
