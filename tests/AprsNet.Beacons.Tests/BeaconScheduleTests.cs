using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Beacons.Tests;

public sealed class BeaconScheduleTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));

    private DateTimeOffset Now => _time.GetUtcNow();

    private static TimeSpan Minutes(double minutes) => TimeSpan.FromMinutes(minutes);

    /// <summary>A random source that always gives the same fraction.</summary>
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }

    [Fact]
    public void AFixedScheduleStartsAfterARandomWaitOfAtMostATenthOfTheInterval()
    {
        var schedule = new FixedBeaconSchedule(Minutes(20), timeProvider: _time, random: new FixedRandom(0.5));

        Assert.Equal(Minutes(2), schedule.StartDelay);
        Assert.Equal(new BeaconDue(Now + Minutes(1), BeaconReason.Start), schedule.Next);
        Assert.Equal(Minutes(1), schedule.DueIn);
        Assert.False(schedule.IsDue);
        Assert.Null(schedule.LastSent);
    }

    [Fact]
    public void TheStartWaitIsAtMostThreeMinutes()
    {
        var schedule = new FixedBeaconSchedule(TimeSpan.FromHours(24), timeProvider: _time, random: new FixedRandom(1));
        Assert.Equal(FixedBeaconSchedule.MaxStartDelay, schedule.StartDelay);
        Assert.Equal(Now + Minutes(3), schedule.Next.At);
    }

    [Fact]
    public void AZeroStartDelayBeaconsAtOnce()
    {
        var schedule = new FixedBeaconSchedule(Minutes(10), TimeSpan.Zero, _time);
        Assert.True(schedule.IsDue);
        Assert.Equal(BeaconReason.Start, schedule.Next.Reason);
    }

    [Fact]
    public void AFixedScheduleBeaconsAtItsInterval()
    {
        var schedule = new FixedBeaconSchedule(Minutes(30), TimeSpan.Zero, _time);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(schedule.IsDue);
            schedule.Sent();
            Assert.Equal(Now, schedule.LastSent);
            Assert.Equal(new BeaconDue(Now + Minutes(30), BeaconReason.Interval), schedule.Next);
            _time.Advance(Minutes(29));
            Assert.False(schedule.IsDue);
            Assert.Equal(Minutes(1), schedule.DueIn);
            _time.Advance(Minutes(1));
        }
    }

    [Fact]
    public void AfterAGapItAsksForOneBeaconAndCarriesOn()
    {
        var schedule = new FixedBeaconSchedule(Minutes(10), TimeSpan.Zero, _time);
        schedule.Sent();

        // Transmit off for an hour: six beacons missed, but only one is asked for.
        _time.Advance(TimeSpan.FromHours(1));
        Assert.True(schedule.IsDue);
        Assert.Equal(TimeSpan.Zero, schedule.DueIn);
        schedule.Sent();
        Assert.False(schedule.IsDue);
        Assert.Equal(Now + Minutes(10), schedule.Next.At);
    }

    [Fact]
    public void AManualSendStartsTheIntervalAgain()
    {
        var schedule = new FixedBeaconSchedule(Minutes(30), TimeSpan.Zero, _time);
        schedule.Sent();
        _time.Advance(Minutes(12));

        schedule.Sent(); // "send position now"
        Assert.Equal(new BeaconDue(Now + Minutes(30), BeaconReason.Interval), schedule.Next);
    }

    [Fact]
    public void AChangeOnAFixedScheduleIsDueNow()
    {
        var schedule = new FixedBeaconSchedule(Minutes(30), TimeSpan.Zero, _time);
        schedule.Sent();
        _time.Advance(Minutes(5));

        schedule.Changed();
        Assert.Equal(new BeaconDue(Now, BeaconReason.Changed), schedule.Next);
        schedule.Sent();
        Assert.Equal(new BeaconDue(Now + Minutes(30), BeaconReason.Interval), schedule.Next);
    }

    [Fact]
    public void ADecayScheduleDoublesUpToItsCeiling()
    {
        var schedule = new DecayBeaconSchedule(Minutes(1), Minutes(30), _time);
        Assert.Equal(new BeaconDue(Now, BeaconReason.Start), schedule.Next);

        var waits = new List<(double Minutes, BeaconReason Reason)>();
        for (var i = 0; i < 8; i++)
        {
            schedule.Sent();
            var next = schedule.Next;
            waits.Add(((next.At - Now).TotalMinutes, next.Reason));
            _time.Advance(next.At - Now);
        }

        Assert.Equal(
            [
                (1, BeaconReason.Decay),
                (2, BeaconReason.Decay),
                (4, BeaconReason.Decay),
                (8, BeaconReason.Decay),
                (16, BeaconReason.Decay),
                (30, BeaconReason.Interval),
                (30, BeaconReason.Interval),
                (30, BeaconReason.Interval),
            ],
            waits
        );
    }

    [Fact]
    public void TheFirstStepIsAParameter()
    {
        var schedule = new DecayBeaconSchedule(TimeSpan.FromSeconds(30), Minutes(10), _time);
        schedule.Sent();
        Assert.Equal(Now + TimeSpan.FromSeconds(30), schedule.Next.At);
        _time.Advance(TimeSpan.FromSeconds(30));
        schedule.Sent();
        Assert.Equal(Now + Minutes(1), schedule.Next.At);
    }

    [Fact]
    public void AChangeStartsTheDecayAgain()
    {
        var schedule = new DecayBeaconSchedule(Minutes(1), Minutes(30), _time);
        for (var i = 0; i < 4; i++)
        {
            schedule.Sent();
            _time.Advance(schedule.Next.At - Now);
        }

        Assert.Equal(Now + Minutes(16), Sent(schedule).At);

        _time.Advance(Minutes(3));
        schedule.Changed();
        Assert.Equal(new BeaconDue(Now, BeaconReason.Changed), schedule.Next);
        Assert.Equal(new BeaconDue(Now + Minutes(1), BeaconReason.Decay), Sent(schedule));
    }

    [Fact]
    public void AManualSendThatShouldRestartTheDecayIsAChangeThenASend()
    {
        var schedule = new DecayBeaconSchedule(Minutes(1), Minutes(30), _time);
        schedule.Sent();
        _time.Advance(Minutes(1));
        schedule.Sent();
        _time.Advance(Minutes(1));

        // Without a change, a manual send just takes the next step.
        Assert.Equal(Now + Minutes(4), Sent(schedule).At);

        schedule.Changed();
        Assert.Equal(Now + Minutes(1), Sent(schedule).At);
    }

    [Fact]
    public void ADecayScheduleAfterAGapAsksForOneBeaconAndTakesTheNextStep()
    {
        var schedule = new DecayBeaconSchedule(Minutes(1), Minutes(30), _time);
        schedule.Sent();
        _time.Advance(TimeSpan.FromHours(2));

        Assert.True(schedule.IsDue);
        Assert.Equal(new BeaconDue(Now + Minutes(2), BeaconReason.Decay), Sent(schedule));
    }

    [Fact]
    public void ACeilingEqualToTheFirstStepIsAFixedInterval()
    {
        var schedule = new DecayBeaconSchedule(Minutes(5), Minutes(5), _time);
        Assert.Equal(new BeaconDue(Now + Minutes(5), BeaconReason.Interval), Sent(schedule));
        _time.Advance(Minutes(5));
        Assert.Equal(new BeaconDue(Now + Minutes(5), BeaconReason.Interval), Sent(schedule));
    }

    [Fact]
    public void ACeilingThatIsNotAPowerOfTwoStepsIsReachedWithoutOvershooting()
    {
        var schedule = new DecayBeaconSchedule(Minutes(1), Minutes(10), _time);
        var waits = new List<double>();
        for (var i = 0; i < 6; i++)
        {
            waits.Add((Sent(schedule).At - Now).TotalMinutes);
            _time.Advance(schedule.Next.At - Now);
        }

        Assert.Equal([1, 2, 4, 8, 10, 10], waits);
    }

    [Fact]
    public void BadSettingsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedBeaconSchedule(TimeSpan.Zero, timeProvider: _time));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedBeaconSchedule(Minutes(10), Minutes(-1), _time));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedBeaconSchedule(Minutes(10), Minutes(11), _time));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecayBeaconSchedule(TimeSpan.Zero, Minutes(10), _time));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecayBeaconSchedule(Minutes(10), Minutes(5), _time));
    }

    private static BeaconDue Sent(BeaconSchedule schedule)
    {
        schedule.Sent();
        return schedule.Next;
    }
}
