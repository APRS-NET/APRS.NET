namespace AprsNet.Packets.Tests;

/// <summary><see cref="NmeaFixTracker.Quality"/> and <see cref="NmeaFixTracker.Satellites"/>, from GGA, GSA, GSV and GST.</summary>
public class NmeaGpsStatusTests
{
    // One second from a multi-system NMEA 4.10 receiver, made up for the test in the order a u-blox sends it: GSA and
    // GSV with system and signal IDs, GPS on two signals (L1 C/A is 1, L5 is 7 … see the NMEA 4.11 tables).
    private static readonly string[] Nmea41 =
    [
        Sentence("GNGGA,120000.00,4903.50000,N,07201.75000,W,1,07,0.95,105.3,M,-32.1,M,,"),
        Sentence("GNGSA,A,3,05,13,15,,,,,,,,,,1.60,0.95,1.29,1"),
        Sentence("GNGSA,A,3,70,71,,,,,,,,,,,1.60,0.95,1.29,2"),
        Sentence("GNGSA,A,3,04,09,,,,,,,,,,,1.60,0.95,1.29,3"),
        Sentence("GPGSV,2,1,05,05,40,083,46,13,62,229,44,15,49,286,42,18,33,051,,1"),
        Sentence("GPGSV,2,2,05,46,35,215,39,1"),
        Sentence("GPGSV,1,1,02,05,40,083,41,13,62,229,40,7"),
        Sentence("GLGSV,1,1,03,70,58,034,38,71,22,301,35,80,05,170,,1"),
        Sentence("GAGSV,1,1,02,04,44,120,42,09,30,250,37,7"),
        Sentence("GNGST,120000.00,8.5,2.1,1.4,85.0,1.2,1.6,2.9"),
    ];

    // An older receiver (NMEA 2.3/4.0): GN GSA with no system ID, so the IDs tell the systems apart (GPS 1–32, SBAS
    // 33–64, GLONASS 65–96).
    private static readonly string[] Nmea23 =
    [
        Sentence("GNGSA,A,3,05,13,46,,,,,,,,,,2.10,1.20,1.72"),
        Sentence("GNGSA,A,3,70,,,,,,,,,,,,2.10,1.20,1.72"),
        Sentence("GPGSV,1,1,03,05,40,083,46,13,62,229,44,46,35,215,39"),
        Sentence("GLGSV,1,1,02,70,58,034,38,71,22,301,35"),
    ];

    /// <summary>A sentence with its checksum.</summary>
    private static string Sentence(string body) => $"${body}*{body.Aggregate(0, (sum, c) => sum ^ c):X2}";

    private static NmeaFixTracker Track(IEnumerable<string> lines)
    {
        var gps = new NmeaFixTracker();
        foreach (var line in lines)
        {
            Assert.True(NmeaSentence.TryParse(line, out var sentence));
            Assert.True(sentence.IsChecksumValid);
            gps.Update(sentence);
        }

        return gps;
    }

    private static GpsSatellite Satellite(
        GnssSystem system,
        int id,
        int? signal,
        double elevation,
        double azimuth,
        double? snr,
        bool used
    ) =>
        new()
        {
            System = system,
            Id = id,
            SignalId = signal,
            ElevationDegrees = elevation,
            AzimuthDegrees = azimuth,
            SnrDbHz = snr,
            Used = used,
        };

    [Fact]
    public void EachSystemAndSignalKeepsItsOwnSatellites()
    {
        var gps = Track(Nmea41);

        Assert.Equal(
            [
                Satellite(GnssSystem.Gps, 5, 1, 40, 83, 46, true),
                Satellite(GnssSystem.Gps, 13, 1, 62, 229, 44, true),
                Satellite(GnssSystem.Gps, 15, 1, 49, 286, 42, true),
                Satellite(GnssSystem.Gps, 18, 1, 33, 51, null, false), // in view, not tracked
                Satellite(GnssSystem.Sbas, 46, 1, 35, 215, 39, false), // GP sentences carry SBAS too
                Satellite(GnssSystem.Gps, 5, 7, 40, 83, 41, true), // the same satellites on L5
                Satellite(GnssSystem.Gps, 13, 7, 62, 229, 40, true),
                Satellite(GnssSystem.Glonass, 70, 1, 58, 34, 38, true),
                Satellite(GnssSystem.Glonass, 71, 1, 22, 301, 35, true),
                Satellite(GnssSystem.Glonass, 80, 1, 5, 170, null, false),
                Satellite(GnssSystem.Galileo, 4, 7, 44, 120, 42, true),
                Satellite(GnssSystem.Galileo, 9, 7, 30, 250, 37, true),
            ],
            gps.Satellites
        );
    }

    [Fact]
    public void QualityGathersTheDopsTheCountsAndTheErrors()
    {
        var gps = Track(Nmea41);

        Assert.Equal(
            new GpsQuality
            {
                SatellitesUsed = 7, // GGA's count, over all systems
                SatellitesInView = 10, // each satellite once, whatever its signals
                Pdop = 1.60,
                Hdop = 0.95,
                Vdop = 1.29,
                LatitudeErrorMeters = 1.2,
                LongitudeErrorMeters = 1.6,
                HorizontalErrorMeters = 2.0, // √(1.2² + 1.6²)
                AltitudeErrorMeters = 2.9,
            },
            gps.Quality with
            {
                HorizontalErrorMeters = Math.Round(gps.Quality.HorizontalErrorMeters!.Value, 9),
            }
        );
    }

    [Fact]
    public void WithoutSystemIdsTheSatelliteIdsTellTheSystems()
    {
        var gps = Track(Nmea23);

        Assert.Equal(
            [
                (GnssSystem.Gps, 5, true),
                (GnssSystem.Gps, 13, true),
                (GnssSystem.Sbas, 46, true),
                (GnssSystem.Glonass, 70, true),
                (GnssSystem.Glonass, 71, false),
            ],
            gps.Satellites.Select(s => (s.System, s.Id, s.Used))
        );
        Assert.All(gps.Satellites, s => Assert.Null(s.SignalId));
        Assert.Equal(4, gps.Quality.SatellitesUsed); // no GGA: counted from the GSAs
        Assert.Equal(2.10, gps.Quality.Pdop);
    }

    [Fact]
    public void AGroupCutShortIsDroppedAndTheLastWholeOneKept()
    {
        var gps = Track(Nmea41);
        var before = gps.Satellites;

        gps.Update(Parse(Sentence("GPGSV,2,1,05,05,41,083,47,13,62,229,44,15,49,286,42,18,33,051,,1")));
        gps.Update(Parse(Sentence("GPGSV,1,1,02,05,40,083,41,13,62,229,40,7"))); // another signal between is fine
        gps.Update(Parse(Sentence("GPGSV,2,1,05,05,42,083,47,13,62,229,44,15,49,286,42,18,33,051,,1"))); // the first 2/2 never came

        Assert.Same(before, gps.Satellites);

        gps.Update(Parse(Sentence("GPGSV,2,2,05,46,35,215,39,1")));

        Assert.Equal(42, gps.Satellites[0].ElevationDegrees);
    }

    [Fact]
    public void SatellitesIsOnlyReplacedWhenItChanges()
    {
        var gps = Track(Nmea41);
        var before = gps.Satellites;

        gps.Update(Parse(Nmea41[8])); // Galileo again, the same

        Assert.Same(before, gps.Satellites);

        gps.Update(Parse(Sentence("GAGSV,1,1,02,04,45,120,42,09,30,250,37,7")));

        Assert.NotSame(before, gps.Satellites);
    }

    [Fact]
    public void LosingTheFixMarksNoneUsed()
    {
        var gps = Track([.. Nmea41, Sentence("GNGSA,A,1,,,,,,,,,,,,,99.99,99.99,99.99,1")]);

        Assert.DoesNotContain(gps.Satellites, s => s.Used);
        Assert.Equal(99.99, gps.Quality.Pdop);
    }

    [Fact]
    public void GsvAndGstChangeTheStatusButNotTheFix()
    {
        var gps = new NmeaFixTracker();

        Assert.False(gps.Update(Parse(Nmea41[7])));
        Assert.False(gps.Update(Parse(Nmea41[9])));
        Assert.Equal(3, gps.Satellites.Count);
        Assert.Equal(2.9, gps.Quality.AltitudeErrorMeters);
        Assert.Equal(new GpsFix(), gps.Fix);
    }

    [Fact]
    public void ADamagedSentenceChangesNothing()
    {
        var gps = new NmeaFixTracker();

        gps.Update(Parse(Nmea41[7].Replace(",58,", ",59,", StringComparison.Ordinal)));

        Assert.Empty(gps.Satellites);
    }

    [Fact]
    public void ResetForgetsTheStatus()
    {
        var gps = Track(Nmea41);

        gps.Reset();

        Assert.Empty(gps.Satellites);
        Assert.Equal(new GpsQuality(), gps.Quality);

        gps.Update(Parse(Nmea41[5])); // the second half of a group from before the reset

        Assert.Empty(gps.Satellites);
    }

    private static NmeaSentence Parse(string line)
    {
        Assert.True(NmeaSentence.TryParse(line, out var sentence));
        return sentence;
    }
}
