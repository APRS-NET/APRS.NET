using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Beacons.Tests;

public sealed class DeadReckoningTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly AprsPosition Origin = new(49.05833, -72.02917);
    private const double Kmh90 = 90 / 1.852; // in knots

    private static DateTimeOffset Minutes(double minutes) => Start.AddMinutes(minutes);

    [Fact]
    public void AStationAt90KmhIsAssumedThreeKilometresOnAfterTwoMinutes()
    {
        var sent = new DeadReckoning(Origin, 90, Kmh90, Start);

        var assumed = sent.PositionAt(Minutes(2));

        Assert.Equal(3, sent.DistanceKmAt(Minutes(2)), 9);
        Assert.Equal(3, Geodesy.DistanceKm(Origin, assumed), 0.03);
        Assert.Equal(90, Geodesy.InitialBearing(Origin, assumed), 0.05);
    }

    [Fact]
    public void StraightLineTravelLeavesNoError()
    {
        var sent = new DeadReckoning(Origin, 135, 50, Start);
        var actual = Geodesy.Destination(Origin, 135, 50 * 1.852 / 6); // ten minutes on

        var error = sent.ErrorAt(actual, Minutes(10));

        Assert.True(error.DistanceKm < 0.001, $"{error.DistanceKm} km");
    }

    [Fact]
    public void ATurnShowsHowFarAndWhichWayTheStationReallyIs()
    {
        var sent = new DeadReckoning(Origin, 90, Kmh90, Start);
        var corner = Geodesy.Destination(Origin, 90, 1.5); // a minute east, then a minute north
        var actual = Geodesy.Destination(corner, 0, 1.5);

        var error = sent.ErrorAt(actual, Minutes(2));

        Assert.Equal(Math.Sqrt(1.5 * 1.5 * 2), error.DistanceKm, 0.02);
        Assert.Equal(315, error.Bearing, 0.5); // north-west of where it is assumed to be
        Assert.Equal(sent.PositionAt(Minutes(2)), error.Assumed);
    }

    [Fact]
    public void AStopLeavesTheAssumedPositionRunningOn()
    {
        var sent = new DeadReckoning(Origin, 90, Kmh90, Start);
        var stopped = Geodesy.Destination(Origin, 90, 1.5); // stopped after a minute

        var error = sent.ErrorAt(stopped, Minutes(5));

        Assert.Equal(6, error.DistanceKm, 0.06);
        Assert.Equal(270, error.Bearing, 0.5);
    }

    [Fact]
    public void TheEstimateCrossesTheAntimeridian()
    {
        var sent = new DeadReckoning(new AprsPosition(-17.5, 179.99), 90, Kmh90, Start);

        var assumed = sent.PositionAt(Minutes(2));

        Assert.True(assumed.Longitude < -179.9, $"{assumed.Longitude}");
        Assert.Equal(3, Geodesy.DistanceKm(sent.Position, assumed), 0.03);
    }

    [Fact]
    public void AStationThatIsNotMovingOrNotYetGoneStaysPut()
    {
        Assert.Equal(Origin, new DeadReckoning(Origin, 90, 0, Start).PositionAt(Minutes(30)));

        var sent = new DeadReckoning(Origin, 90, Kmh90, Start);
        Assert.Equal(Origin, sent.PositionAt(Start));
        Assert.Equal(Origin, sent.PositionAt(Minutes(-5)));
        Assert.Equal(0, sent.DistanceKmAt(Minutes(-5)));
    }

    [Fact]
    public void WhatCannotBeACourseOrSpeedIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeadReckoning(Origin, 361, 10, Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeadReckoning(Origin, -1, 10, Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeadReckoning(Origin, 90, -1, Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeadReckoning(Origin, 90, double.NaN, Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeadReckoning(Origin, 90, double.PositiveInfinity, Start));
        Assert.Equal(360, new DeadReckoning(Origin, 360, 10, Start).Course);
    }

    [Theory]
    [InlineData("!4903.50N/07201.75W>088/036", true)]
    [InlineData("!4903.50N/07201.75W>360/000", true)]
    [InlineData("!4903.50N/07201.75W>000/036", false)] // course 000 may mean unknown
    [InlineData("!4903.50N/07201.75W>.../...", false)]
    [InlineData("!4903.50N/07201.75W>On the road", false)]
    public void AReportGivesADeadReckoningWhenItHasACourseAndSpeed(string information, bool expected)
    {
        var report = (PositionedPayload)AprsPacket.Parse("N0CALL-9>APRS:" + information).Payload!;

        Assert.Equal(expected, DeadReckoning.TryFromReport(report, Start, out var sent));

        if (expected)
        {
            Assert.Equal(report.Position, sent.Position);
            Assert.Equal(report.Course!.Value, sent.Course);
            Assert.Equal(report.SpeedKnots!.Value, sent.SpeedKnots);
            Assert.Equal(Start, sent.At);
        }
    }

    [Fact]
    public void DriftingTooFarAsksForABeaconNow()
    {
        var time = new FakeTimeProvider(Start);
        var schedule = new FixedBeaconSchedule(TimeSpan.FromMinutes(30), TimeSpan.Zero, time);
        schedule.Sent();
        var sent = new DeadReckoning(Origin, 90, Kmh90, Start);

        time.Advance(TimeSpan.FromMinutes(3));
        var stopped = Geodesy.Destination(Origin, 90, 1.5);
        if (sent.ErrorAt(stopped, time.GetUtcNow()).DistanceKm > 2)
        {
            schedule.Drifted();
        }

        Assert.Equal(new BeaconDue(time.GetUtcNow(), BeaconReason.Drift), schedule.Next);
        schedule.Sent();
        Assert.Equal(new BeaconDue(time.GetUtcNow().AddMinutes(30), BeaconReason.Interval), schedule.Next);
    }

    [Fact]
    public void DriftStartsADecayScheduleAgain()
    {
        var time = new FakeTimeProvider(Start);
        var schedule = new DecayBeaconSchedule(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30), time);
        for (var i = 0; i < 4; i++)
        {
            schedule.Sent();
            time.SetUtcNow(schedule.Next.At);
        }

        schedule.Drifted();
        Assert.Equal(BeaconReason.Drift, schedule.Next.Reason);
        schedule.Sent();

        Assert.Equal(time.GetUtcNow().AddMinutes(1), schedule.Next.At);
    }
}
