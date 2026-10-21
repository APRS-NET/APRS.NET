using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Gps.Tests;

/// <summary><see cref="GpsdConnection.Quality"/>, <see cref="GpsdConnection.Satellites"/> and <see cref="GpsdConnection.LineRead"/>.</summary>
public sealed class GpsdStatusTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // A gpsd 3.25 session with a multi-system u-blox, made up for the test in the shape gpsd_json(5) documents: a SKY
    // with the satellites and DOPs, a TPV with its error estimates, then a SKY with the DOPs alone, as u-blox drivers
    // send, and another TPV.
    private static readonly string[] Session =
    [
        """{"class":"VERSION","release":"3.25","rev":"3.25","proto_major":3,"proto_minor":15}""",
        """{"class":"SKY","device":"/dev/ttyACM0","time":"2026-10-20T14:00:00.000Z","xdop":0.61,"ydop":0.72,"vdop":1.29,"tdop":0.83,"hdop":0.95,"gdop":1.80,"pdop":1.60,"nSat":5,"uSat":4,"satellites":[{"PRN":5,"gnssid":0,"svid":5,"sigid":0,"el":40.0,"az":83.0,"ss":46.0,"used":true},{"PRN":18,"gnssid":0,"svid":18,"sigid":0,"el":33.0,"az":51.0,"ss":0.0,"used":false},{"PRN":46,"gnssid":1,"svid":133,"el":35.0,"az":215.0,"ss":39.0,"used":false},{"PRN":70,"gnssid":6,"svid":6,"sigid":0,"el":58.0,"az":34.0,"ss":38.0,"used":true},{"PRN":304,"gnssid":2,"svid":4,"sigid":0,"el":44.0,"az":120.0,"ss":42.0,"used":true},{"PRN":5,"gnssid":0,"svid":5,"sigid":7,"el":40.0,"az":83.0,"ss":41.0,"used":true}]}""",
        """{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"2026-10-20T14:00:00.000Z","lat":49.058333333,"lon":-72.029166667,"altMSL":105.3,"speed":10.0,"track":90.0,"eph":3.1,"epx":1.9,"epy":2.4,"epv":4.8,"eps":0.5}""",
        """{"class":"SKY","device":"/dev/ttyACM0","time":"2026-10-20T14:00:01.000Z","hdop":1.05,"pdop":1.70,"vdop":1.36}""",
        """{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"2026-10-20T14:00:01.000Z","lat":49.058333333,"lon":-72.029,"altMSL":105.4,"eph":3.3}""",
    ];

    private static GpsdConnection Connection(FakeGpsd gpsd) =>
        new(_ => Task.FromResult<Stream>(gpsd), new GpsdOptions { TimeProvider = new FakeTimeProvider() });

    private static async Task<GpsFix> NextAsync(IAsyncEnumerator<GpsFix> fixes)
    {
        Assert.True(await fixes.MoveNextAsync().AsTask().WaitAsync(Wait, TestContext.Current.CancellationToken));
        return fixes.Current;
    }

    [Fact]
    public async Task SkyAndTpvGiveTheSatellitesAndTheQuality()
    {
        var gpsd = new FakeGpsd();
        gpsd.Send(Session);
        await using var connection = Connection(gpsd);
        await using var fixes = connection
            .ReadFixesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await NextAsync(fixes);

        Assert.Equal(
            [
                new GpsSatellite
                {
                    System = GnssSystem.Gps,
                    Id = 5,
                    SignalId = 0,
                    ElevationDegrees = 40,
                    AzimuthDegrees = 83,
                    SnrDbHz = 46,
                    Used = true,
                },
                new GpsSatellite
                {
                    System = GnssSystem.Gps,
                    Id = 18,
                    SignalId = 0,
                    ElevationDegrees = 33,
                    AzimuthDegrees = 51,
                    SnrDbHz = null, // gpsd's 0: not tracked
                },
                new GpsSatellite
                {
                    System = GnssSystem.Sbas,
                    Id = 46,
                    ElevationDegrees = 35,
                    AzimuthDegrees = 215,
                    SnrDbHz = 39,
                },
                new GpsSatellite
                {
                    System = GnssSystem.Glonass,
                    Id = 70,
                    SignalId = 0,
                    ElevationDegrees = 58,
                    AzimuthDegrees = 34,
                    SnrDbHz = 38,
                    Used = true,
                },
                new GpsSatellite
                {
                    System = GnssSystem.Galileo,
                    Id = 304,
                    SignalId = 0,
                    ElevationDegrees = 44,
                    AzimuthDegrees = 120,
                    SnrDbHz = 42,
                    Used = true,
                },
                new GpsSatellite
                {
                    System = GnssSystem.Gps,
                    Id = 5,
                    SignalId = 7,
                    ElevationDegrees = 40,
                    AzimuthDegrees = 83,
                    SnrDbHz = 41,
                    Used = true,
                },
            ],
            connection.Satellites
        );
        Assert.Equal(
            new GpsQuality
            {
                SatellitesUsed = 4,
                SatellitesInView = 5,
                Pdop = 1.60,
                Hdop = 0.95,
                Vdop = 1.29,
                Tdop = 0.83,
                Gdop = 1.80,
                HorizontalErrorMeters = 3.1,
                LongitudeErrorMeters = 1.9,
                LatitudeErrorMeters = 2.4,
                AltitudeErrorMeters = 4.8,
                SpeedErrorKnots = 0.5 / (1852.0 / 3600),
            },
            connection.Quality
        );
    }

    [Fact]
    public async Task ASkyWithTheDopsAloneKeepsTheSatellites()
    {
        var gpsd = new FakeGpsd();
        gpsd.Send(Session);
        await using var connection = Connection(gpsd);
        await using var fixes = connection
            .ReadFixesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await NextAsync(fixes);
        var satellites = connection.Satellites;
        await NextAsync(fixes);

        Assert.Same(satellites, connection.Satellites);
        Assert.Equal(1.70, connection.Quality.Pdop);
        Assert.Equal(1.05, connection.Quality.Hdop);
        Assert.Equal(0.83, connection.Quality.Tdop); // not sent again: kept
        Assert.Equal(4, connection.Quality.SatellitesUsed);
        Assert.Equal(3.3, connection.Quality.HorizontalErrorMeters);
        Assert.Null(connection.Quality.AltitudeErrorMeters); // this TPV had none: unknown, not the last one
    }

    [Fact]
    public async Task AnOlderGpsdsSatellitesAreToldApartByPrn()
    {
        var gpsd = new FakeGpsd();
        gpsd.Send(
            """{"class":"SKY","hdop":1.2,"satellites":[{"PRN":5,"el":40,"az":83,"ss":46,"used":true},{"PRN":46,"el":35,"az":215,"ss":39,"used":true},{"PRN":70,"el":58,"az":34,"ss":38,"used":false},{"PRN":210,"el":20,"az":10,"ss":30,"used":false},{"PRN":304,"el":44,"az":120,"ss":42,"used":false}]}""",
            """{"class":"TPV","mode":2,"lat":49.05,"lon":-72.03}"""
        );
        await using var connection = Connection(gpsd);
        await using var fixes = connection
            .ReadFixesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await NextAsync(fixes);

        Assert.Equal(
            [GnssSystem.Gps, GnssSystem.Sbas, GnssSystem.Glonass, GnssSystem.BeiDou, GnssSystem.Galileo],
            connection.Satellites.Select(s => s.System)
        );
        Assert.Equal(2, connection.Quality.SatellitesUsed); // counted from the list
        Assert.Equal(5, connection.Quality.SatellitesInView);
    }

    [Fact]
    public async Task EveryLineGpsdSendsIsRaised()
    {
        var gpsd = new FakeGpsd();
        gpsd.Send(Session[0], "not a report", Session[2]);
        await using var connection = Connection(gpsd);
        var lines = new List<string>();
        connection.LineRead += (_, e) => lines.Add(e.Line);
        await using var fixes = connection
            .ReadFixesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await NextAsync(fixes);

        Assert.Equal([Session[0], "not a report", Session[2]], lines);
    }
}
