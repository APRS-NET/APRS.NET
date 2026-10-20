using System.Net;
using System.Net.Sockets;
using System.Text;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Gps.Tests;

public sealed class GpsdConnectionTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // A gpsd 3.25 session in the shape gpsd_json(5) documents, made up for the test at APRS101's example position:
    // the greeting, the GPS starting with no fix, then a 2D fix, then a 3D fix.
    private static readonly string[] Session =
    [
        """{"class":"VERSION","release":"3.25","rev":"3.25","proto_major":3,"proto_minor":15}""",
        """{"class":"DEVICES","devices":[{"class":"DEVICE","path":"/dev/ttyACM0","driver":"u-blox","activated":"2026-10-20T14:00:00.000Z","native":1,"bps":9600,"parity":"N","stopbits":1,"cycle":1.00}]}""",
        """{"class":"WATCH","enable":true,"json":true,"nmea":false,"raw":0,"scaled":false,"timing":false,"split24":false,"pps":false}""",
        """{"class":"TPV","device":"/dev/ttyACM0","mode":1,"time":"2026-10-20T14:00:01.000Z","ept":0.005}""",
        """{"class":"SKY","device":"/dev/ttyACM0","time":"2026-10-20T14:00:05.000Z","hdop":2.40,"satellites":[{"PRN":5,"el":40.0,"az":83.0,"ss":46.0,"used":true},{"PRN":13,"el":62.0,"az":229.0,"ss":44.0,"used":true},{"PRN":15,"el":49.0,"az":286.0,"ss":42.0,"used":true},{"PRN":18,"el":33.0,"az":51.0,"ss":20.0,"used":false}]}""",
        """{"class":"TPV","device":"/dev/ttyACM0","mode":2,"time":"2026-10-20T14:00:06.000Z","ept":0.005,"lat":49.058333333,"lon":-72.029166667,"track":90.0000,"speed":25.000,"eph":12.400}""",
        """{"class":"SKY","device":"/dev/ttyACM0","time":"2026-10-20T14:00:10.000Z","hdop":0.92,"nSat":12,"uSat":9}""",
        """{"class":"TPV","device":"/dev/ttyACM0","status":1,"mode":3,"time":"2026-10-20T14:00:11.000Z","ept":0.005,"lat":49.058333333,"lon":-72.027355000,"altHAE":73.200,"altMSL":105.300,"alt":105.300,"track":90.5000,"magtrack":75.2,"speed":25.050,"climb":0.000,"eph":3.100,"epv":4.800}""",
    ];

    private readonly FakeTimeProvider _time = new();

    private GpsdConnection Connection(Func<CancellationToken, Task<Stream>> connect, string? device = null) =>
        new(
            connect,
            new GpsdOptions
            {
                Device = device,
                ReconnectDelay = TimeSpan.FromSeconds(2),
                MaxReconnectDelay = TimeSpan.FromSeconds(30),
                TimeProvider = _time,
            }
        );

    private static async Task<GpsFix> NextAsync(IAsyncEnumerator<GpsFix> fixes)
    {
        Assert.True(await fixes.MoveNextAsync().AsTask().WaitAsync(Wait, TestContext.Current.CancellationToken));
        return fixes.Current;
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
    public async Task ARecordedSessionGivesNoFixThenA2DThenA3DFix()
    {
        var gpsd = new FakeGpsd();
        gpsd.Send(Session);
        await using var connection = Connection(_ => Task.FromResult<Stream>(gpsd));
        await using var fixes = connection
            .ReadFixesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var none = await NextAsync(fixes);
        var twoD = await NextAsync(fixes);
        var threeD = await NextAsync(fixes);

        Assert.Equal(
            new GpsFix
            {
                Mode = GpsFixMode.NoFix,
                UtcDate = new DateOnly(2026, 10, 20),
                UtcTime = new TimeOnly(14, 0, 1),
            },
            none
        );

        Assert.Equal(GpsFixMode.Fix2D, twoD.Mode);
        Assert.Equal(new AprsPosition(49.058333333, -72.029166667), twoD.Position);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 14, 0, 6, TimeSpan.Zero), twoD.Time);
        Assert.Equal(48.596, twoD.SpeedKnots!.Value, 0.001); // 25 m/s
        Assert.Equal(90, twoD.Course);
        Assert.Null(twoD.AltitudeMeters); // a 2D fix has no altitude
        Assert.Equal(3, twoD.Satellites); // the older SKY form: the satellites marked used
        Assert.Equal(2.4, twoD.Hdop);

        Assert.Equal(GpsFixMode.Fix3D, threeD.Mode);
        Assert.Equal(105.3, threeD.AltitudeMeters); // above mean sea level, not the ellipsoid
        Assert.Equal(90.5, threeD.Course);
        Assert.Equal(9, threeD.Satellites);
        Assert.Equal(0.92, threeD.Hdop);

        Assert.Equal("3.25", connection.Release);
        Assert.Equal("?WATCH={\"enable\":true,\"json\":true}\n", gpsd.Written);
        Assert.True(connection.IsConnected);
        Assert.Equal(GpsdConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task AskingForOneGpsNamesItsDevice()
    {
        var gpsd = new FakeGpsd();
        gpsd.Send(Session[0], """{"class":"ERROR","message":"Can't open /dev/ttyUSB9."}""", Session[3]);
        await using var connection = Connection(_ => Task.FromResult<Stream>(gpsd), device: "/dev/ttyUSB9");
        await using var fixes = connection
            .ReadFixesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await NextAsync(fixes);

        Assert.Equal("?WATCH={\"enable\":true,\"json\":true,\"device\":\"/dev/ttyUSB9\"}\n", gpsd.Written);
        Assert.Equal("Can't open /dev/ttyUSB9.", connection.LastError);
    }

    [Fact]
    public async Task OlderGpsdsAltitudeAndLinesThatArentReportsAreHandled()
    {
        var gpsd = new FakeGpsd();
        gpsd.Send(
            "not json",
            "[1,2,3]",
            """{"class":"PPS","device":"/dev/ttyACM0"}""",
            """{"class":"TPV","mode":3,"lat":49.05,"lon":-72.03,"alt":101.5,"speed":"fast"}""",
            """{"class":"TPV","mode":2,"lat":123.0,"lon":-72.03}"""
        );
        await using var connection = Connection(_ => Task.FromResult<Stream>(gpsd));
        await using var fixes = connection
            .ReadFixesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var older = await NextAsync(fixes);
        var impossible = await NextAsync(fixes);

        Assert.Equal(101.5, older.AltitudeMeters);
        Assert.Null(older.SpeedKnots);
        Assert.Null(older.Time);
        Assert.Null(impossible.Position);
        Assert.Equal(GpsFixMode.Fix2D, impossible.Mode);
    }

    [Fact]
    public async Task ReadingCarriesOnAfterGpsdRestarts()
    {
        var first = new FakeGpsd();
        var second = new FakeGpsd();
        var attempts = 0;
        var daemons = new Queue<FakeGpsd>([first, second]);
        await using var connection = Connection(_ =>
            ++attempts == 2
                ? throw new SocketException((int)SocketError.ConnectionRefused) // still starting up
                : Task.FromResult<Stream>(daemons.Dequeue())
        );
        var lost = new List<GpsdConnectionStateChangedEventArgs>();
        connection.StateChanged += (_, e) =>
        {
            if (e.State == GpsdConnectionState.Reconnecting)
            {
                lost.Add(e);
            }
        };

        first.Send(Session[0], Session[5]);
        first.End();
        second.Send(Session[0], Session[7]);

        await using var fixes = connection
            .ReadFixesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.Equal(GpsFixMode.Fix2D, (await NextAsync(fixes)).Mode);
        var next = NextAsync(fixes);

        await WaitUntil(() => lost.Count == 1);
        Assert.Null(lost[0].Error); // gpsd closed the connection
        Assert.Equal(TimeSpan.FromSeconds(2), lost[0].RetryIn);
        Assert.False(connection.IsConnected);
        _time.Advance(TimeSpan.FromSeconds(2));

        await WaitUntil(() => lost.Count == 2);
        Assert.IsType<SocketException>(lost[1].Error);
        Assert.Equal(TimeSpan.FromSeconds(4), lost[1].RetryIn); // doubled after a failed attempt
        _time.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal(GpsFixMode.Fix3D, (await next).Mode);
        Assert.Equal(1, connection.Reconnects);
        Assert.Equal("?WATCH={\"enable\":true,\"json\":true}\n", second.Written); // asked again
    }

    [Fact]
    public async Task ARealGpsdIsReadAcrossARestart()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var token = TestContext.Current.CancellationToken;
        var gpsd = Task.Run(
            async () =>
            {
                foreach (var tpv in new[] { Session[5], Session[7] })
                {
                    using var client = await listener.AcceptTcpClientAsync(token);
                    var stream = client.GetStream();
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(Session[0] + "\r\n"), token);
                    var watch = new byte[256];
                    _ = await stream.ReadAsync(watch, token); // gpsd waits for ?WATCH before sending reports
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(tpv + "\r\n"), token);
                    await Task.Delay(50, token);
                }
            },
            token
        );

        await using var connection = new GpsdConnection(
            new GpsdOptions
            {
                Host = "127.0.0.1",
                Port = port,
                ReconnectDelay = TimeSpan.FromMilliseconds(20),
            }
        );
        await using var fixes = connection.ReadFixesAsync(token).GetAsyncEnumerator(token);

        Assert.Equal(GpsFixMode.Fix2D, (await NextAsync(fixes)).Mode);
        Assert.Equal(GpsFixMode.Fix3D, (await NextAsync(fixes)).Mode);
        Assert.Equal(1, connection.Reconnects);
        Assert.Equal("127.0.0.1:" + port, connection.Server);
        await gpsd;
    }

    [Fact]
    public async Task OnlyOneReadRunsAndDisposingEndsIt()
    {
        var gpsd = new FakeGpsd();
        var connection = Connection(_ => Task.FromResult<Stream>(gpsd));
        var states = new List<GpsdConnectionState>();
        connection.StateChanged += (_, e) => states.Add(e.State);
        await using var fixes = connection
            .ReadFixesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = fixes.MoveNextAsync().AsTask();
        await WaitUntil(() => connection.IsConnected);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in connection.ReadFixesAsync(TestContext.Current.CancellationToken)) { }
        });

        await connection.DisposeAsync();
        Assert.False(await next.WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.Equal(
            [GpsdConnectionState.Connecting, GpsdConnectionState.Connected, GpsdConnectionState.Closed],
            states
        );
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in connection.ReadFixesAsync(TestContext.Current.CancellationToken)) { }
        });
    }

    [Fact]
    public void UnusableSettingsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new GpsdConnection(""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpsdConnection("localhost", 0));
        Assert.Throws<ArgumentException>(() => new GpsdConnection(new GpsdOptions { Device = " " }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GpsdConnection(new GpsdOptions { ReconnectDelay = TimeSpan.Zero })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GpsdConnection(
                new GpsdOptions
                {
                    ReconnectDelay = TimeSpan.FromSeconds(10),
                    MaxReconnectDelay = TimeSpan.FromSeconds(5),
                }
            )
        );

        var defaults = new GpsdOptions();
        Assert.Equal(("localhost", 2947), (defaults.Host, defaults.Port));
        Assert.Equal(TimeSpan.FromSeconds(2), defaults.ReconnectDelay);
        Assert.Equal(TimeSpan.FromSeconds(60), defaults.MaxReconnectDelay);
    }
}
