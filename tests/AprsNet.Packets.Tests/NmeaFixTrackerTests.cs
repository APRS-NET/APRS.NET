namespace AprsNet.Packets.Tests;

public class NmeaFixTrackerTests
{
    // A multi-system receiver's output for two seconds either side of midnight, then a second without a fix. Made up
    // for the test (at APRS101's example position), in the order a u-blox sends it.
    private static readonly string[] Capture =
    [
        "$GNRMC,235958.00,A,4903.50000,N,07201.75000,W,48.600,90.00,191026,,,A*61",
        "$GNVTG,90.00,T,,M,48.600,N,90.007,K,A*1E",
        "$GNGGA,235958.00,4903.50000,N,07201.75000,W,1,09,0.92,105.3,M,-32.1,M,,*75",
        "$GNGSA,A,3,05,13,15,18,23,24,,,,,,,1.65,0.92,1.37*1D",
        "$GPGSV,2,1,07,05,40,083,46,13,62,229,44,15,49,286,42,18,33,051,40*70",
        "$GNGLL,4903.50000,N,07201.75000,W,235958.00,A,A*68",
        "$GNRMC,235959.00,A,4903.50000,N,07201.68500,W,48.700,90.50,191026,,,A*6D",
        "$GNVTG,90.50,T,,M,48.700,N,90.192,K,A*17",
        "$GNGGA,235959.00,4903.50000,N,07201.68500,W,1,08,1.10,105.8,M,-32.1,M,,*7C",
        "$GNGSA,A,3,05,13,15,18,23,,,,,,,,1.80,1.10,1.42*19",
        "$GNRMC,000000.00,V,,,,,,,201026,,,N*64",
        "$GNGGA,000000.00,,,,,0,00,99.99,,,,,,*78",
        "$GNGSA,A,1,,,,,,,,,,,,,99.99,99.99,99.99*2E",
    ];

    private static NmeaSentence Sentence(string text)
    {
        Assert.True(NmeaSentence.TryParse(text, out var sentence));
        return sentence;
    }

    /// <summary>The fix after each line of the capture.</summary>
    private static List<GpsFix> Track(IEnumerable<string> lines)
    {
        var gps = new NmeaFixTracker();
        var fixes = new List<GpsFix>();
        foreach (var line in lines)
        {
            gps.Update(Sentence(line));
            fixes.Add(gps.Fix);
        }

        return fixes;
    }

    [Fact]
    public void TheSentencesOfOneSecondMakeOneFix()
    {
        var fix = Track(Capture.Take(6))[^1];

        Assert.Equal(49.0583333, fix.Position!.Value.Latitude, 0.0000001);
        Assert.Equal(-72.0291667, fix.Position!.Value.Longitude, 0.0000001);
        Assert.Equal(
            new GpsFix
            {
                Mode = GpsFixMode.Fix3D,
                UtcTime = new TimeOnly(23, 59, 58),
                UtcDate = new DateOnly(2026, 10, 19),
                SpeedKnots = 48.6,
                Course = 90,
                AltitudeMeters = 105.3,
                Satellites = 6, // GSA's count, the last one sent (GGA said 9)
                Hdop = 0.92,
            },
            fix with
            {
                Position = null,
            }
        );
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 23, 59, 58, TimeSpan.Zero), fix.Time);
    }

    [Fact]
    public void ANewSecondStartsANewFix()
    {
        var fixes = Track(Capture.Take(10));

        Assert.Equal(GpsFixMode.Fix, fixes[6].Mode); // the RMC alone, until GSA says 3D again
        Assert.Null(fixes[6].AltitudeMeters); // nothing kept from the second before
        Assert.Equal(new TimeOnly(23, 59, 59), fixes[9].UtcTime);
        Assert.Equal(-72.02808333333334, fixes[9].Position!.Value.Longitude, 0.0000001);
        Assert.Equal(105.8, fixes[9].AltitudeMeters);
        Assert.Equal(GpsFixMode.Fix3D, fixes[9].Mode);
        Assert.Equal(5, fixes[9].Satellites);
    }

    [Fact]
    public void LosingTheFixDropsThePosition()
    {
        var fix = Track(Capture)[^1];

        Assert.Equal(GpsFixMode.NoFix, fix.Mode);
        Assert.Null(fix.Position);
        Assert.Null(fix.SpeedKnots);
        Assert.Equal(new DateOnly(2026, 10, 20), fix.UtcDate);
        Assert.Equal(new TimeOnly(0, 0), fix.UtcTime);
    }

    [Fact]
    public void TheDateMovesOnAtMidnightWithoutAnRmc()
    {
        var fixes = Track([
            Capture[0],
            Capture[2],
            "$GNGGA,000000.00,4903.50000,N,07201.62000,W,1,08,1.10,105.8,M,-32.1,M,,*72",
        ]);

        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), fixes[^1].Time);
    }

    [Fact]
    public void ADamagedSentenceIsIgnored()
    {
        var gps = new NmeaFixTracker();
        gps.Update(Sentence(Capture[0]));
        var before = gps.Fix;

        var changed = gps.Update(Sentence(Capture[2].Replace("4903.5", "4803.5", StringComparison.Ordinal)));

        Assert.False(changed);
        Assert.Same(before, gps.Fix);
    }

    [Fact]
    public void UpdateSaysWhetherTheFixChanged()
    {
        var gps = new NmeaFixTracker();

        Assert.True(gps.Update(Sentence(Capture[0])));
        Assert.False(gps.Update(Sentence(Capture[0]))); // the same again
        Assert.False(gps.Update(Sentence(Capture[4]))); // GSV gives no fix
        Assert.True(gps.Update(Sentence(Capture[2])));

        gps.Reset();
        Assert.Equal(new GpsFix(), gps.Fix);
    }

    /// <summary>
    /// A Kenwood TH-D72A's GPS before it has a fix, as it sent them on 2026-10-22 (the GGA and VTG rebuilt from cut-off
    /// copies): every sentence says there is no fix, and some carry the radio's placeholder position, 36°N 136°E. None
    /// of them is a position.
    /// </summary>
    [Fact]
    public void AKenwoodWithoutAFixGivesNoPositionFromItsPlaceholder()
    {
        string[] noFix =
        [
            "$GPGGA,221910.461,3600.0000,N,13600.0000,E,0,00,99.9,0000.0,M,0.0,M,,0000*6F",
            "$GPGLL,3600.0000,N,13600.0000,E,221910.461,V,N*4A",
            "$GPGSA,A,1,,,,,,,,,,,,,99.9,99.9,99.9*09",
            "$GPRMC,221910.461,V,3600.0000,N,13600.0000,E,,,221026,,*1A",
            "$GPVTG,,T,,M,,N,,K,N*2C",
        ];

        foreach (var line in noFix)
        {
            var alone = Track([line]).Single();
            Assert.Null(alone.Position);
            Assert.NotEqual(GpsFixMode.Fix, alone.Mode);
        }

        var together = Track(noFix).Last();
        Assert.Null(together.Position);
        Assert.Equal(GpsFixMode.NoFix, together.Mode);
    }
}
