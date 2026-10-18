using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Messaging.Tests;

public sealed class BulletinScheduleTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Start);

    /// <summary>Sends each time the schedule is due, until it finishes; returns the minutes from the start.</summary>
    private List<double> SendWhenDue(BulletinSchedule schedule)
    {
        var sends = new List<double>();
        while (schedule.Next is { } next)
        {
            _time.SetUtcNow(next);
            Assert.True(schedule.IsDue);
            sends.Add((next - Start).TotalMinutes);
            schedule.Sent();
        }

        return sends;
    }

    [Fact]
    public void ABulletinGoesOftenAtFirstThenEveryTwentyMinutesForFourHours()
    {
        var schedule = BulletinSchedule.ForBulletin(_time);
        double[] expected = [0, 1, 3, 7, 15, 31, 51, 71, 91, 111, 131, 151, 171, 191, 211, 231];

        Assert.Equal(expected.Select(m => Start.AddMinutes(m)), schedule.Upcoming());
        Assert.Equal(expected, SendWhenDue(schedule));
        Assert.True(schedule.IsFinished);
        Assert.Null(schedule.DueIn);
        Assert.Empty(schedule.Upcoming());
        Assert.Equal(Start.AddHours(4), schedule.EndsAt);
    }

    [Fact]
    public void AnAnnouncementGoesLessOftenForTwoDays()
    {
        var schedule = BulletinSchedule.ForAnnouncement(_time);

        var sends = SendWhenDue(schedule);

        Assert.Equal([0, 10, 30, 70, 130, 190], sends.Take(6));
        Assert.All(sends.Skip(4).Zip(sends.Skip(5)), pair => Assert.Equal(60, pair.Second - pair.First));
        Assert.True(sends[^1] < TimeSpan.FromDays(2).TotalMinutes);
        Assert.Equal(50, sends.Count); // 0, 10, 30, 70, then hourly from 130 to 2830 minutes
    }

    [Fact]
    public void TheFirstSendIsDueAtOnce()
    {
        var schedule = BulletinSchedule.ForBulletin(_time);

        Assert.Equal(Start, schedule.Next);
        Assert.True(schedule.IsDue);
        Assert.Equal(TimeSpan.Zero, schedule.DueIn);
        Assert.Null(schedule.LastSent);
    }

    [Fact]
    public void EachWaitIsTimedFromTheActualSendWithoutCatchingUp()
    {
        var schedule = BulletinSchedule.ForBulletin(_time);
        schedule.Sent();
        Assert.Equal(TimeSpan.FromMinutes(1), schedule.DueIn);

        _time.Advance(TimeSpan.FromMinutes(30)); // transmit was off: one send is overdue, not several
        Assert.True(schedule.IsDue);
        Assert.Equal(TimeSpan.Zero, schedule.DueIn);
        schedule.Sent();

        Assert.Equal(Start.AddMinutes(30), schedule.LastSent);
        Assert.Equal(Start.AddMinutes(32), schedule.Next);
    }

    [Fact]
    public void RestartSendsNowAndRunsTheWholeScheduleAgain()
    {
        var schedule = BulletinSchedule.ForBulletin(_time);
        SendWhenDue(schedule);
        Assert.True(schedule.IsFinished);

        var edited = _time.GetUtcNow();
        schedule.Restart();

        Assert.Equal(edited, schedule.StartedAt);
        Assert.Equal(edited, schedule.Next);
        schedule.Sent();
        Assert.Equal(edited.AddMinutes(1), schedule.Next);
        Assert.Equal(edited.AddHours(4), schedule.EndsAt);
    }

    [Fact]
    public void StopEndsTheSchedule()
    {
        var schedule = BulletinSchedule.ForBulletin(_time);
        schedule.Sent();

        schedule.Stop();

        Assert.True(schedule.IsFinished);
        Assert.False(schedule.IsDue);
        Assert.Null(schedule.Next);
        _time.Advance(TimeSpan.FromHours(1));
        schedule.Sent(); // a send by hand after Stop doesn't start it again
        Assert.True(schedule.IsFinished);
    }

    [Fact]
    public void NoSendIsDueOnceTheLifetimeHasPassed()
    {
        var schedule = new BulletinSchedule(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(30),
            _time
        );
        schedule.Sent();

        _time.Advance(TimeSpan.FromMinutes(30));

        Assert.True(schedule.IsFinished);
        Assert.Empty(schedule.Upcoming());
    }

    [Fact]
    public void TheSettingsAreChecked()
    {
        var minute = TimeSpan.FromMinutes(1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BulletinSchedule(TimeSpan.FromSeconds(10), minute, TimeSpan.FromHours(1), _time)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BulletinSchedule(minute * 2, minute, TimeSpan.FromHours(1), _time)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulletinSchedule(minute, minute, TimeSpan.Zero, _time));
    }

    [Fact]
    public void ABulletinIsSentWithoutAnIdWhenDue()
    {
        using var station = new MessagingStation(Callsign.Parse("N0CALL"), new() { TimeProvider = _time });
        var schedule = BulletinSchedule.ForBulletin(_time);

        var bulletin = station.Send(Callsign.Parse("BLN1"), "Club meeting Tuesday 7pm", withId: false);
        schedule.Sent();

        Assert.Null(bulletin.MessageId);
        Assert.Equal(OutgoingMessageState.Sent, bulletin.State);
        Assert.Equal(Start.AddMinutes(1), schedule.Next);
    }
}
