using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AprsNet.AprsIsCli.Tests;

/// <summary><c>aprscli gps</c> against a gpsd and an NMEA source on a local socket.</summary>
public sealed class GpsTests : IDisposable
{
    private static readonly string NewLine = Environment.NewLine;

    // A gpsd 3.25 session made up for the test at APRS101's example position, in the shape gpsd_json(5) documents.
    private static readonly string[] Gpsd =
    [
        """{"class":"VERSION","release":"3.25","rev":"3.25","proto_major":3,"proto_minor":15}""",
        """{"class":"TPV","device":"/dev/ttyACM0","mode":1,"time":"2026-10-20T14:00:01.000Z"}""",
        """{"class":"SKY","device":"/dev/ttyACM0","hdop":0.92,"pdop":1.60,"nSat":3,"uSat":2,"satellites":[{"PRN":5,"gnssid":0,"svid":5,"el":40.0,"az":83.0,"ss":46.0,"used":true},{"PRN":70,"gnssid":6,"svid":6,"el":58.0,"az":34.0,"ss":38.0,"used":true},{"PRN":18,"gnssid":0,"svid":18,"el":33.0,"az":51.0,"ss":0,"used":false}]}""",
        """{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"2026-10-20T14:00:02.000Z","lat":49.058333333,"lon":-72.029166667,"altMSL":105.3,"speed":25.0,"track":90.0,"eph":3.1}""",
    ];

    // A GPS's NMEA for three seconds, made up for the test at APRS101's example position.
    private static readonly string[] Nmea =
    [
        Sentence("GPRMC,140001.00,A,4903.50000,N,07201.75000,W,48.600,90.00,201026,,,A"),
        Sentence("GPGGA,140001.00,4903.50000,N,07201.75000,W,1,02,0.92,105.3,M,-32.1,M,,"),
        Sentence("GPGSA,A,3,05,13,,,,,,,,,,,1.60,0.92,1.29"),
        Sentence("GPGSV,1,1,03,05,40,083,46,13,62,229,44,18,33,051,"),
        Sentence("GPGST,140001.00,8.5,2.1,1.4,85.0,1.2,1.6,2.9"),
        Sentence("GPRMC,140002.00,A,4903.50000,N,07201.68500,W,48.700,90.50,201026,,,A"),
        Sentence("GPGGA,140002.00,4903.50000,N,07201.68500,W,1,02,0.95,105.8,M,-32.1,M,,"),
        Sentence("GPRMC,140003.00,V,,,,,,,201026,,,N"),
    ];

    private readonly TcpListener _source = new(IPAddress.Loopback, 0);

    public GpsTests()
    {
        _source.Start();
    }

    private string Address => $"127.0.0.1:{((IPEndPoint)_source.LocalEndpoint).Port}";

    public void Dispose() => _source.Dispose();

    private static string Sentence(string body) => $"${body}*{body.Aggregate(0, (sum, c) => sum ^ c):X2}";

    /// <summary>Accepts a connection and sends <paramref name="lines"/>, then closes it.</summary>
    private async Task Serve(string[] lines)
    {
        using var client = await _source.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        var stream = client.GetStream();
        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(string.Concat(lines.Select(l => l + "\r\n"))),
            TestContext.Current.CancellationToken
        );
        client.Client.Shutdown(SocketShutdown.Send);
    }

    [Fact]
    public async Task GpsdFixesArePrintedOneALine()
    {
        var run = new CliHarness().RunAsync(
            ["gps", "--gpsd", Address, "-n", "2"],
            TestContext.Current.CancellationToken
        );
        await Serve(Gpsd);
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "2026-10-20 14:00:01Z no fix"
                + NewLine
                + "2026-10-20 14:00:02Z 3D 49.05833 -72.02917 105.3 m 48.6 kn 90° 2/3 sats HDOP 0.92 ±3.1 m"
                + NewLine,
            result.Out
        );
        Assert.Contains($"connected to gpsd at {Address}", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SatellitesAreListedWhenTheyChange()
    {
        var run = new CliHarness().RunAsync(
            ["gps", "--gpsd", Address, "-n", "2", "--satellites"],
            TestContext.Current.CancellationToken
        );
        await Serve(Gpsd);
        var result = await run;

        Assert.Equal(
            [
                "2026-10-20 14:00:01Z no fix",
                "2026-10-20 14:00:02Z 3D 49.05833 -72.02917 105.3 m 48.6 kn 90° 2/3 sats HDOP 0.92 ±3.1 m",
                "  GPS        5 el 40 az  83 46 dB-Hz used",
                "  GLONASS   70 el 58 az  34 38 dB-Hz used",
                "  GPS       18 el 33 az  51 not tracked",
            ],
            result.Out.Split(NewLine, StringSplitOptions.RemoveEmptyEntries)
        );
    }

    [Fact]
    public async Task RawPrintsTheLinesAsRead()
    {
        var run = new CliHarness().RunAsync(
            ["gps", "--gpsd", Address, "-n", "2", "--raw"],
            TestContext.Current.CancellationToken
        );
        await Serve(Gpsd);
        var result = await run;

        Assert.Equal(string.Concat(Gpsd.Select(line => line + NewLine)), result.Out);
    }

    [Fact]
    public async Task NmeaGivesAFixForEachSecond()
    {
        var run = new CliHarness().RunAsync(
            ["gps", "--nmea", Address, "-n", "2"],
            TestContext.Current.CancellationToken
        );
        await Serve(Nmea);
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "2026-10-20 14:00:01Z 3D 49.05833 -72.02917 105.3 m 48.6 kn 90° 2/3 sats HDOP 0.92 ±2.0 m"
                + NewLine
                + "2026-10-20 14:00:02Z fix 49.05833 -72.02808 105.8 m 48.7 kn 91° 2/3 sats HDOP 0.95 ±2.0 m"
                + NewLine,
            result.Out
        );
    }

    [Fact]
    public async Task NmeaCarriesOnWhenTheSourceComesBack()
    {
        var run = new CliHarness().RunAsync(
            ["gps", "--nmea", Address, "-n", "2"],
            TestContext.Current.CancellationToken
        );
        await Serve(Nmea[..6]); // second 1, then the connection drops
        await Serve(Nmea[5..]);
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.Out.Split(NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains(
            $"lost {Address} (it closed the connection); trying again in",
            result.Error,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task GpsdIsAskedForTheDeviceGiven()
    {
        var run = new CliHarness().RunAsync(
            ["gps", "--gpsd", Address, "--device", "/dev/ttyACM0", "-n", "1"],
            TestContext.Current.CancellationToken
        );
        using var client = await _source.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        var buffer = new byte[256];
        var read = await client.GetStream().ReadAsync(buffer, TestContext.Current.CancellationToken);
        await client
            .GetStream()
            .WriteAsync(Encoding.ASCII.GetBytes(Gpsd[1] + "\n"), TestContext.Current.CancellationToken);
        var result = await run;

        Assert.Equal(
            "?WATCH={\"enable\":true,\"json\":true,\"device\":\"/dev/ttyACM0\"}\n",
            Encoding.ASCII.GetString(buffer, 0, read)
        );
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(new[] { "gps" }, "Give one GPS source")]
    [InlineData(new[] { "gps", "--gpsd", "--nmea", "host:10110" }, "Give one GPS source")]
    [InlineData(new[] { "gps", "--nmea", "host" }, "Use host:port")]
    [InlineData(new[] { "gps", "--nmea", "host:99999" }, "Use host:port")]
    [InlineData(
        new[] { "gps", "--nmea", "host:10110", "--device", "/dev/ttyACM0" },
        "--device only applies with --gpsd"
    )]
    [InlineData(new[] { "gps", "--gpsd", "-n", "0" }, "--count must be at least 1")]
    [InlineData(new[] { "gps", "--gpsd", "-d", "soon" }, "--duration 'soon' is not valid")]
    public async Task BadOptionsAreRefused(string[] args, string message)
    {
        var result = await new CliHarness().RunAsync(args, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }
}
