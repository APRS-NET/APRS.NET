# AprsNet.Gps

Read a GPS through [gpsd](https://gpsd.io), on this machine or another, as `GpsFix` values from `AprsNet.Packets`:
position, time, speed, course, altitude, satellites, HDOP, and whether there is a 2D or 3D fix.

```csharp
using AprsNet.Gps;

await using var gps = new GpsdConnection("raspberrypi.local"); // port 2947 by default
gps.StateChanged += (_, e) => Console.WriteLine($"gpsd: {e.State} {e.Error?.Message}");

await foreach (var fix in gps.ReadFixesAsync(cancellationToken))
{
    if (fix.Position is { } here)
    {
        Console.WriteLine($"{fix.Mode} {here.Latitude:0.00000} {here.Longitude:0.00000} {fix.SpeedKnots:0.#} kn");
    }
}
```

`GpsdConnection` asks gpsd for JSON reports and yields a fix for each position report (TPV). When gpsd restarts, or
the machine it runs on goes away, it reconnects, waiting `ReconnectDelay` (2 seconds) and doubling up to
`MaxReconnectDelay` (60 seconds), and the read carries on. `State`, `StateChanged` and `Reconnects` say how the
connection is doing; `Release` is gpsd's version, and `LastError` the last error gpsd reported.

`GpsdOptions` also sets the `Device` to read when gpsd has several GPSs, the connect timeout and the `TimeProvider`.

- Without a fix (`GpsFixMode.NoFix`) there is no position. Altitude, above mean sea level, comes only with a 3D fix.
- Satellites and HDOP come from gpsd's latest sky report, which it sends less often, so they can be a few seconds old.
- Speed is in knots, as in APRS: gpsd's metres per second are converted.

For a GPS status view, `Quality` and `Satellites` hold what gpsd last said, updated before each fix is yielded:

- `Quality` (`GpsQuality`): the satellites used and in view, the DOPs (PDOP, HDOP, VDOP, TDOP, GDOP) from the sky
  report, and gpsd's error estimates in metres (`eph`, `epx`, `epy`, `epv`, at 95% confidence) and in knots (`eps`)
  from the position report.
- `Satellites`: each `GpsSatellite` in view with its system (`GnssSystem`: GPS, GLONASS, Galileo, BeiDou …), PRN,
  signal, elevation, azimuth, signal-to-noise ratio, and whether the fix uses it. A sky report with the DOPs alone,
  as some drivers send, keeps the last list.
- `LineRead` is raised for each line gpsd sends, for a console showing its output.

```csharp
gps.LineRead += (_, e) => console.Add(e.Line);
await foreach (var fix in gps.ReadFixesAsync(cancellationToken))
{
    var q = gps.Quality;
    Console.WriteLine($"{q.SatellitesUsed}/{q.SatellitesInView} satellites, HDOP {q.Hdop}, ±{q.HorizontalErrorMeters} m");
}
```

A GPS on a serial port without gpsd sends NMEA sentences instead: read those with `NmeaSentence` and
`NmeaFixTracker` in `AprsNet.Packets`, which give the same `GpsFix`, `GpsQuality` and `GpsSatellite`.
