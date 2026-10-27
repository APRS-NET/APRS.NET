using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Gps.Tests;

/// <summary>
/// <see cref="GpsMetrics"/> over a recorded NMEA session and a gpsd session: each one has a fix lost and regained, and
/// a system with satellites in view but none used.
/// </summary>
public sealed class GpsMetricsTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // Made up for the test in a u-blox's order. GPS satellites 5 and 13 are used; BeiDou 11 and 12 are in view, unused.
    private static string[] Second(string time, int quality, int mode) =>
        [
            Sentence($"GNGGA,{time},4903.50000,N,07201.75000,W,{quality},02,0.95,105.3,M,-32.1,M,,"),
            Sentence(mode == 1 ? "GNGSA,A,1,,,,,,,,,,,,,,,,1" : $"GNGSA,A,{mode},05,13,,,,,,,,,,,1.60,0.95,1.29,1"),
            Sentence("GPGSV,1,1,02,05,40,083,46,13,62,229,40,1"),
            Sentence("GBGSV,1,1,02,11,30,100,30,12,20,200,,1"),
            Sentence($"GNGST,{time},8.5,2.1,1.4,85.0,1.2,1.6,2.9"),
        ];

    private static string Sentence(string body) => $"${body}*{body.Aggregate(0, (sum, c) => sum ^ c):X2}";

    /// <summary>Every measurement on the GPS meter tagged with <paramref name="gps"/>.</summary>
    private sealed class Listener : IDisposable
    {
        private readonly MeterListener _listener = new();

        public Listener(string gps)
        {
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == GpsMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(gps, i, v, t));
            _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Add(gps, i, v, t));
            _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(gps, i, v, t));
            _listener.Start();
        }

        public ConcurrentQueue<(string Name, double Value, Dictionary<string, string> Tags)> Seen { get; } = new();

        /// <summary>The values of <paramref name="name"/> with all of <paramref name="tags"/>.</summary>
        public double[] Values(string name, params (string Key, string Value)[] tags) =>
            [
                .. Seen.Where(m => m.Name == name && tags.All(t => m.Tags.GetValueOrDefault(t.Key) == t.Value))
                    .Select(m => m.Value),
            ];

        public void Dispose() => _listener.Dispose();

        private void Add(
            string gps,
            Instrument instrument,
            double value,
            ReadOnlySpan<KeyValuePair<string, object?>> tags
        )
        {
            var dictionary = new Dictionary<string, string>();
            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value?.ToString() ?? "";
            }

            if (dictionary.GetValueOrDefault("gps") == gps)
            {
                Seen.Enqueue((instrument.Name, value, dictionary));
            }
        }
    }

    private static string UniqueGps() => "gps-" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public void AnNmeaSessionRecordsEachInstrument()
    {
        var name = UniqueGps();
        using var listener = new Listener(name);
        var time = new FakeTimeProvider();
        var metrics = new GpsMetrics(name, GpsErrorConfidence.OneSigma, time);
        var gps = new NmeaFixTracker();
        void Play(string[] sentences)
        {
            foreach (var line in sentences)
            {
                Assert.True(NmeaSentence.TryParse(line, out var sentence));
                gps.Update(sentence);
                metrics.Record(gps);
            }

            time.Advance(TimeSpan.FromSeconds(1));
        }

        // Three seconds of 3D, two with no fix, then 3D again.
        Play(Second("120000.00", 1, 3));
        Play(Second("120001.00", 1, 3));
        Play(Second("120002.00", 1, 3));
        Play(Second("120003.00", 0, 1));
        Play(Second("120004.00", 0, 1));
        Play(Second("120005.00", 1, 3));

        Assert.Equal(3, listener.Values("aprsnet.gps.fix.time", ("mode", "3d")).Sum());
        Assert.Equal(2, listener.Values("aprsnet.gps.fix.time", ("mode", "none")).Sum());
        Assert.Equal([1], listener.Values("aprsnet.gps.fix.losses"));

        Assert.Equal(2, listener.Values("aprsnet.gps.satellites.in_view", ("system", "beidou"))[^1]);
        Assert.Equal(0, listener.Values("aprsnet.gps.satellites.used", ("system", "beidou"))[^1]);
        Assert.Equal(2, listener.Values("aprsnet.gps.satellites.used", ("system", "gps"))[^1]);
        Assert.Equal(1.60, listener.Values("aprsnet.gps.dop", ("kind", "pdop"))[^1]);
        Assert.Equal(1.29, listener.Values("aprsnet.gps.dop", ("kind", "vdop"))[^1]);
        Assert.Equal(1.2, listener.Values("aprsnet.gps.error", ("axis", "latitude"), ("confidence", "one_sigma"))[^1]);
        Assert.Equal(2.9, listener.Values("aprsnet.gps.error", ("axis", "vertical"))[^1]);
        Assert.Equal(43, listener.Values("aprsnet.gps.signal")[^1]); // GPS 5 and 13, used: (46 + 40) / 2
        Assert.All(listener.Seen, m => Assert.DoesNotContain(m.Tags.Keys, k => k is "lat" or "lon" or "position"));
    }

    [Fact]
    public void AQuietGpsCountsAtMostTheMaxInterval()
    {
        var name = UniqueGps();
        using var listener = new Listener(name);
        var time = new FakeTimeProvider();
        var metrics = new GpsMetrics(name, GpsErrorConfidence.OneSigma, time);
        var fix = new GpsFix { Mode = GpsFixMode.Fix3D };

        metrics.Record(fix, new GpsQuality(), []);
        time.Advance(TimeSpan.FromMinutes(5));
        metrics.Record(fix, new GpsQuality(), []);

        Assert.Equal(GpsMetrics.MaxInterval.TotalSeconds, listener.Values("aprsnet.gps.fix.time").Sum());
    }

    [Fact]
    public void AResetIsNotAFixLost()
    {
        var name = UniqueGps();
        using var listener = new Listener(name);
        var time = new FakeTimeProvider();
        var metrics = new GpsMetrics(name, GpsErrorConfidence.Percent95, time);

        metrics.Record(new GpsFix { Mode = GpsFixMode.Fix3D }, new GpsQuality(), []);
        metrics.Reset();
        time.Advance(TimeSpan.FromSeconds(5));
        metrics.Record(new GpsFix { Mode = GpsFixMode.NoFix }, new GpsQuality(), []);

        Assert.Empty(listener.Values("aprsnet.gps.fix.losses"));
        Assert.Empty(listener.Values("aprsnet.gps.fix.time"));
    }

    [Fact]
    public void ASystemThatLeavesViewGoesToZero()
    {
        var name = UniqueGps();
        using var listener = new Listener(name);
        var metrics = new GpsMetrics(name, GpsErrorConfidence.Percent95);
        var fix = new GpsFix { Mode = GpsFixMode.Fix3D };
        var gps = new GpsSatellite
        {
            System = GnssSystem.Gps,
            Id = 5,
            Used = true,
            SnrDbHz = 40,
        };

        metrics.Record(fix, new GpsQuality(), [gps, new GpsSatellite { System = GnssSystem.Galileo, Id = 4 }]);
        metrics.Record(fix, new GpsQuality(), [gps]);

        Assert.Equal([1, 0], listener.Values("aprsnet.gps.satellites.in_view", ("system", "galileo")));
    }

    [Fact]
    public async Task AGpsdSessionRecordsEachInstrumentTaggedWithTheServer()
    {
        var host = UniqueGps();
        using var listener = new Listener($"{host}:2947");
        var gpsd = new FakeGpsd();
        gpsd.Send(
            """{"class":"VERSION","release":"3.25","rev":"3.25","proto_major":3,"proto_minor":15}""",
            """{"class":"SKY","device":"/dev/ttyACM0","hdop":0.95,"pdop":1.60,"vdop":1.29,"tdop":0.83,"gdop":1.80,"satellites":[{"PRN":5,"gnssid":0,"svid":5,"el":40.0,"az":83.0,"ss":46.0,"used":true},{"PRN":13,"gnssid":0,"svid":13,"el":62.0,"az":229.0,"ss":40.0,"used":true},{"PRN":70,"gnssid":6,"svid":6,"el":58.0,"az":34.0,"ss":38.0,"used":false}]}""",
            """{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"2026-10-20T14:00:00.000Z","lat":49.05,"lon":-72.02,"eph":3.1,"epx":1.9,"epy":2.4,"epv":4.8}""",
            """{"class":"TPV","device":"/dev/ttyACM0","mode":1,"time":"2026-10-20T14:00:01.000Z"}""",
            """{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"2026-10-20T14:00:02.000Z","lat":49.05,"lon":-72.02,"eph":3.3}"""
        );
        await using var connection = new GpsdConnection(
            _ => Task.FromResult<Stream>(gpsd),
            new GpsdOptions { Host = host, TimeProvider = new FakeTimeProvider() }
        );
        await using var fixes = connection
            .ReadFixesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await fixes.MoveNextAsync().AsTask().WaitAsync(Wait, TestContext.Current.CancellationToken));
        }

        Assert.Equal([1], listener.Values("aprsnet.gps.fix.losses"));
        Assert.NotEmpty(listener.Values("aprsnet.gps.fix.time", ("mode", "3d")));
        Assert.NotEmpty(listener.Values("aprsnet.gps.fix.time", ("mode", "none")));
        Assert.Equal(1, listener.Values("aprsnet.gps.satellites.in_view", ("system", "glonass"))[^1]);
        Assert.Equal(0, listener.Values("aprsnet.gps.satellites.used", ("system", "glonass"))[^1]);
        Assert.Equal(2, listener.Values("aprsnet.gps.satellites.used", ("system", "gps"))[^1]);
        foreach (var kind in new[] { "pdop", "hdop", "vdop", "tdop", "gdop" })
        {
            Assert.NotEmpty(listener.Values("aprsnet.gps.dop", ("kind", kind)));
        }

        Assert.Equal(
            3.3,
            listener.Values("aprsnet.gps.error", ("axis", "horizontal"), ("confidence", "95_percent"))[^1]
        );
        Assert.Equal(4.8, listener.Values("aprsnet.gps.error", ("axis", "vertical"))[^1]);
        Assert.Equal(43, listener.Values("aprsnet.gps.signal")[^1]);
    }
}
