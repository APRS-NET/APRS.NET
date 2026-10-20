using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Beacons.Tests;

/// <summary>
/// Simulated drives with Dire Wolf's default settings (60 mph every 90 s, 5 mph every 30 min, turns of 30° plus 255 ÷
/// mph, at least 10 s apart), a fix every second, beaconing whenever the schedule says one is due.
/// </summary>
public sealed class SmartBeaconScheduleTests
{
    private static readonly AprsPosition Origin = new(49.05833, -72.02917);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    private static double Mph(double mph) => mph / 1.150779448;

    /// <summary>A stretch of the drive: how long, how fast and which way.</summary>
    private sealed record Leg(TimeSpan Duration, double SpeedKnots, double? Course);

    private static Leg Seconds(double seconds, double mph, double? course) =>
        new(TimeSpan.FromSeconds(seconds), Mph(mph), course);

    /// <summary>Drives the legs, a fix a second, and returns each beacon's time from the start and its reason.</summary>
    private List<(double Seconds, BeaconReason Reason)> Drive(SmartBeaconSchedule schedule, params Leg[] legs)
    {
        var start = _time.GetUtcNow();
        var position = Origin;
        var beacons = new List<(double, BeaconReason)>();
        foreach (var leg in legs)
        {
            for (var t = TimeSpan.Zero; t < leg.Duration; t += TimeSpan.FromSeconds(1))
            {
                schedule.Update(
                    new GpsFix
                    {
                        Mode = GpsFixMode.Fix3D,
                        Position = position,
                        SpeedKnots = leg.SpeedKnots,
                        Course = leg.Course,
                    }
                );
                if (schedule.IsDue)
                {
                    beacons.Add(((_time.GetUtcNow() - start).TotalSeconds, schedule.Next.Reason));
                    schedule.Sent();
                }

                position = Geodesy.Destination(position, leg.Course ?? 0, leg.SpeedKnots * 1.852 / 3600);
                _time.Advance(TimeSpan.FromSeconds(1));
            }
        }

        return beacons;
    }

    private static double[] Times(List<(double Seconds, BeaconReason Reason)> beacons) =>
        [.. beacons.Select(b => b.Seconds)];

    [Fact]
    public void AParkedStationBeaconsEveryHalfHourHoweverItsCourseWanders()
    {
        var schedule = new SmartBeaconSchedule(timeProvider: _time);
        var legs = Enumerable
            .Range(0, 2 * 3600 / 10)
            .Select(i => Seconds(10, 0.4, i * 97 % 360)) // a stopped GPS's course is noise
            .ToArray();

        var beacons = Drive(schedule, legs);

        Assert.Equal([0, 1800, 3600, 5400], Times(beacons));
        Assert.Equal(
            [BeaconReason.Start, BeaconReason.Interval, BeaconReason.Interval, BeaconReason.Interval],
            beacons.Select(b => b.Reason)
        );
    }

    [Fact]
    public void SteadyHighwayDrivingBeaconsAtTheFastRate()
    {
        var beacons = Drive(new SmartBeaconSchedule(timeProvider: _time), Seconds(600, 70, 90));

        Assert.Equal([0, 90, 180, 270, 360, 450, 540], Times(beacons));
    }

    [Fact]
    public void BetweenTheSpeedsTheIntervalShrinksAsTheSpeedRises()
    {
        var schedule = new SmartBeaconSchedule(timeProvider: _time);

        Assert.Equal(TimeSpan.FromMinutes(30), schedule.IntervalAt(Mph(4.9)));
        Assert.Equal(TimeSpan.FromSeconds(1080), schedule.IntervalAt(Mph(5)));
        Assert.Equal(180, schedule.IntervalAt(Mph(30)).TotalSeconds, 3);
        Assert.Equal(90, schedule.IntervalAt(Mph(60)).TotalSeconds, 3);
        Assert.Equal(TimeSpan.FromSeconds(90), schedule.IntervalAt(Mph(120)));

        Assert.Equal([0, 180, 360, 540], Times(Drive(schedule, Seconds(600, 30, 0))));
    }

    [Fact]
    public void ACityGridBeaconsAtEachCorner()
    {
        // 25 mph: a turn is more than 30 + 255/25 = 40.2 degrees, and the interval is 216 s.
        var beacons = Drive(
            new SmartBeaconSchedule(timeProvider: _time),
            Seconds(60, 25, 0),
            Seconds(60, 25, 90),
            Seconds(60, 25, 0),
            Seconds(300, 25, 270)
        );

        Assert.Equal([0, 60, 120, 180, 396], Times(beacons));
        Assert.Equal(
            [BeaconReason.Start, BeaconReason.Turn, BeaconReason.Turn, BeaconReason.Turn, BeaconReason.Interval],
            beacons.Select(b => b.Reason)
        );
    }

    [Fact]
    public void ASlowStationMustTurnFurther()
    {
        var schedule = new SmartBeaconSchedule(timeProvider: _time);
        Assert.Equal(30 + (255.0 / 25), schedule.TurnThresholdAt(Mph(25)), 9);
        Assert.Equal(double.PositiveInfinity, schedule.TurnThresholdAt(Mph(0.9)));

        // A 35-degree bend is a turn at 70 mph (threshold 33.6) but not at 25 mph (40.2).
        Assert.Equal([0, 30], Times(Drive(schedule, Seconds(30, 70, 0), Seconds(30, 70, 35))));
        var slow = new SmartBeaconSchedule(timeProvider: _time);
        Assert.Equal([0], Times(Drive(slow, Seconds(30, 25, 0), Seconds(30, 25, 35))));
    }

    [Fact]
    public void TurnsAcrossNorthCountTheShortWayRound()
    {
        var beacons = Drive(new SmartBeaconSchedule(timeProvider: _time), Seconds(30, 70, 350), Seconds(30, 70, 20));

        Assert.Equal([0], Times(beacons)); // 30 degrees, not 330
    }

    [Fact]
    public void ATurnSoonAfterABeaconWaitsForTheTurnTime()
    {
        var beacons = Drive(new SmartBeaconSchedule(timeProvider: _time), Seconds(3, 70, 0), Seconds(30, 70, 90));

        Assert.Equal([(0, BeaconReason.Start), (10, BeaconReason.Turn)], beacons);
    }

    [Fact]
    public void AStopAndStartBeaconsAsSoonAsTheStationIsMovingAgain()
    {
        var beacons = Drive(
            new SmartBeaconSchedule(timeProvider: _time),
            Seconds(300, 70, 90), // beacons at 0, 90, 180, 270
            Seconds(1200, 0, null), // stopped: the next is half an hour after the last
            Seconds(300, 70, 90) // off again at 1500 s, long past 270 + 90
        );

        Assert.Equal([0, 90, 180, 270, 1500, 1590, 1680, 1770], Times(beacons));
    }

    [Fact]
    public void ChangedAsksForABeaconNowWhateverTheSpeed()
    {
        var schedule = new SmartBeaconSchedule(timeProvider: _time);
        Drive(schedule, Seconds(1, 0, null));
        _time.Advance(TimeSpan.FromMinutes(5));

        schedule.Changed();
        var beacons = Drive(schedule, Seconds(1, 0, null)); // a fix doesn't put the beacon off again

        Assert.Equal([(0, BeaconReason.Changed)], beacons);
        Assert.Equal(new BeaconDue(schedule.LastSent!.Value.AddMinutes(30), BeaconReason.Interval), schedule.Next);
    }

    [Fact]
    public void AFixWithoutAPositionIsIgnored()
    {
        var schedule = new SmartBeaconSchedule(timeProvider: _time);
        Drive(schedule, Seconds(1, 70, 0));
        var next = schedule.Next;

        schedule.Update(new GpsFix { Mode = GpsFixMode.NoFix, SpeedKnots = 0 });

        Assert.Equal(next, schedule.Next);
    }

    [Fact]
    public void TheDefaultsAreDireWolfsInKnots()
    {
        var settings = new SmartBeaconSettings();

        Assert.Equal(Mph(5), settings.SlowSpeedKnots, 9);
        Assert.Equal(Mph(60), settings.FastSpeedKnots, 9);
        Assert.Equal(TimeSpan.FromMinutes(30), settings.SlowRate);
        Assert.Equal(TimeSpan.FromSeconds(90), settings.FastRate);
        Assert.Equal(30, settings.TurnAngle);
        Assert.Equal(221.59, settings.TurnSlope, 2);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.TurnTime);
    }

    public static TheoryData<SmartBeaconSettings, string> BadSettings =>
        new()
        {
            {
                new() { SlowSpeedKnots = 0 },
                "SlowSpeedKnots"
            },
            {
                new() { FastSpeedKnots = 3 },
                "FastSpeedKnots"
            },
            {
                new() { FastRate = TimeSpan.Zero },
                "FastRate"
            },
            {
                new() { SlowRate = TimeSpan.FromSeconds(60) },
                "SlowRate"
            },
            {
                new() { TurnAngle = 181 },
                "TurnAngle"
            },
            {
                new() { TurnSlope = -1 },
                "TurnSlope"
            },
            {
                new() { TurnTime = TimeSpan.FromSeconds(-1) },
                "TurnTime"
            },
        };

    [Theory]
    [MemberData(nameof(BadSettings))]
    public void UnusableSettingsAreRefused(SmartBeaconSettings settings, string name)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => new SmartBeaconSchedule(settings));
        Assert.Equal(name, error.ParamName);
    }
}
