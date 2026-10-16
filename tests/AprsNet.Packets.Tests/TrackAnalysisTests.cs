using System.Globalization;

namespace AprsNet.Packets.Tests;

public sealed class TrackAnalysisTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly AprsPosition Denver = new(39.7392, -104.9903);

    private static TrackReport R(AprsPosition position, double seconds, double? reportedSeconds = null) =>
        new(position, T0.AddSeconds(seconds), reportedSeconds is { } s ? T0.AddSeconds(s) : null);

    private static AprsPosition East(double km) => Geodesy.Destination(Denver, 90, km);

    [Fact]
    public void TheFirstReportHasNothingToCompareWith()
    {
        var check = TrackAnalysis.Check([], R(Denver, 0));
        Assert.Equal(TrackAnomalies.None, check.Anomalies);
        Assert.Null(check.Previous);
        Assert.Null(check.SpeedKmh);
    }

    [Fact]
    public void ANormalDriveRaisesNothing()
    {
        // 100 km/h, a report a minute, then parked and beaconing every 30 minutes.
        List<TrackReport> track = [];
        for (var minute = 0; minute <= 20; minute++)
        {
            var report = R(East(minute * 100.0 / 60), minute * 60);
            Assert.Equal(TrackAnomalies.None, TrackAnalysis.Check(track, report).Anomalies);
            track.Add(report);
        }

        var parked = track[^1].Position;
        for (var beacon = 1; beacon <= 3; beacon++)
        {
            var report = R(parked, 1200 + beacon * 1800);
            Assert.Equal(TrackAnomalies.None, TrackAnalysis.Check(track, report).Anomalies);
            track.Add(report);
        }
    }

    [Fact]
    public void AJumpTooFastToBeRealIsFlaggedWithItsSpeed()
    {
        var check = TrackAnalysis.Check([R(Denver, 0)], R(East(100), 60));
        Assert.Equal(TrackAnomalies.ImpossibleSpeed, check.Anomalies);
        Assert.Equal(100, check.DistanceKm!.Value, tolerance: 0.5);
        Assert.Equal(6000, check.SpeedKmh!.Value, tolerance: 30);
        Assert.Equal(TimeSpan.FromMinutes(1), check.Interval);
    }

    [Fact]
    public void TheSpeedLimitIsTheCallers()
    {
        // 2,000 km/h: a balloon in a jet stream by the default limit, but not a car by a map's limit for cars.
        TrackReport[] track = [R(Denver, 0)];
        var report = R(East(2000.0 / 60), 60);
        Assert.Equal(TrackAnomalies.None, TrackAnalysis.Check(track, report).Anomalies);
        var cars = new TrackAnalysisOptions { MaxSpeedKmh = 1000 };
        Assert.Equal(TrackAnomalies.ImpossibleSpeed, TrackAnalysis.Check(track, report, cars).Anomalies);
    }

    [Fact]
    public void MovingWithNoTimeBetweenReportsIsInfinitelyFast()
    {
        var check = TrackAnalysis.Check([R(Denver, 0)], R(East(1), 0));
        Assert.Equal(TrackAnomalies.ImpossibleSpeed | TrackAnomalies.RapidRepeat, check.Anomalies);
        Assert.Equal(double.PositiveInfinity, check.SpeedKmh);
    }

    [Theory]
    [InlineData(20, false)] // Inside the duplicate window: a copy, not a repeat.
    [InlineData(30, true)]
    [InlineData(119, true)]
    [InlineData(120, false)]
    [InlineData(600, false)] // A fixed station beaconing every 10 minutes.
    public void TheSamePositionResentTooSoonIsARepeat(double seconds, bool flagged)
    {
        TrackReport[] track = [R(Denver, 0)];
        var check = TrackAnalysis.Check(track, R(Denver, seconds));
        Assert.Equal(flagged ? TrackAnomalies.RepeatedPosition : TrackAnomalies.None, check.Anomalies);
        Assert.Equal(flagged ? track[0] : null, check.RepeatOf);
        Assert.Equal(0, check.DistanceKm);
    }

    [Fact]
    public void AWeatherStationIsNeverARepeat()
    {
        var received = T0.AddMinutes(5);
        var report = TrackReport.FromPacket(P("N0CALL>APRS:!3933.40N/10522.41W_220/004g005t077"), received)!.Value;
        Assert.True(report.HasWeather);
        var check = TrackAnalysis.Check([report with { ReceivedAt = T0 }], report);
        Assert.Equal(TrackAnomalies.None, check.Anomalies);
    }

    [Fact]
    public void AStationThatHasNotMovedIsOnlyCheckedAgainstTheRepeatInterval()
    {
        // Parked and beaconing every 5 minutes: each report matches every earlier one, but none is a late copy.
        TrackReport[] track = [R(Denver, 0), R(Denver, 300), R(Denver, 600)];
        Assert.Equal(TrackAnomalies.None, TrackAnalysis.Check(track, R(Denver, 900)).Anomalies);
    }

    [Theory]
    [InlineData(1799, true)]
    [InlineData(1800, false)] // Too late to be a copy: the station came back.
    public void ALateCopyIsLookedForWithinTheLateCopyInterval(double seconds, bool flagged)
    {
        TrackReport[] track = [R(Denver, 0), R(East(5), seconds - 60)];
        var check = TrackAnalysis.Check(track, R(Denver, seconds));
        Assert.Equal(flagged, check.Anomalies.HasFlag(TrackAnomalies.RepeatedPosition));
    }

    [Fact]
    public void TheRepeatIntervalIsTheCallers()
    {
        var options = new TrackAnalysisOptions { RepeatedPositionInterval = TimeSpan.FromMinutes(30) };
        var check = TrackAnalysis.Check([R(Denver, 0)], R(Denver, 600), options);
        Assert.Equal(TrackAnomalies.RepeatedPosition, check.Anomalies);
    }

    [Fact]
    public void ALateCopyOfAnEarlierPositionIsARepeatNotAJumpBack()
    {
        TrackReport[] track = [R(Denver, 0), R(East(2), 60), R(East(4), 120)];
        var check = TrackAnalysis.Check(track, R(Denver, 150));
        Assert.Equal(TrackAnomalies.RepeatedPosition, check.Anomalies);
        Assert.Equal(track[0], check.RepeatOf);
        Assert.Equal(track[2], check.Previous);
        Assert.Null(check.DistanceKm);
        Assert.Null(check.SpeedKmh);
    }

    [Fact]
    public void ReportsUnderASecondApartAreRapidRepeats()
    {
        var check = TrackAnalysis.Check([R(Denver, 0)], R(East(0.01), 0.5));
        Assert.Equal(TrackAnomalies.RapidRepeat, check.Anomalies);
        Assert.Equal(TimeSpan.FromSeconds(0.5), check.Interval);
    }

    [Fact]
    public void ACopyChangedInTransitIsNotARapidRepeat()
    {
        // Same position and same reported time: a digipeater changed the packet, the station didn't send it twice.
        var check = TrackAnalysis.Check([R(Denver, 0, reportedSeconds: -5)], R(Denver, 0.8, reportedSeconds: -5));
        Assert.Equal(TrackAnomalies.None, check.Anomalies);
    }

    [Fact]
    public void AReportedTimeEarlierThanTheLatestOneRunsBackwards()
    {
        TrackReport[] track = [R(Denver, 0, reportedSeconds: 0), R(East(1), 60)];
        var check = TrackAnalysis.Check(track, R(East(2), 120, reportedSeconds: -60));
        Assert.Equal(TrackAnomalies.TimeBackwards, check.Anomalies);
        Assert.Equal(TrackAnomalies.None, TrackAnalysis.Check(track, R(East(2), 120, reportedSeconds: 120)).Anomalies);
    }

    [Fact]
    public void ReportsWithoutTimesNeverRunBackwards()
    {
        var check = TrackAnalysis.Check([R(Denver, 0, reportedSeconds: 0)], R(East(1), 60));
        Assert.Equal(TrackAnomalies.None, check.Anomalies);
    }

    [Fact]
    public void ADelayedReportIsTimedByTheLongerGap()
    {
        // Sent a minute apart but arriving five seconds apart: 1.5 km in a minute, not in five seconds.
        var check = TrackAnalysis.Check(
            [R(Denver, 0, reportedSeconds: 0)],
            R(East(1.5), 5, reportedSeconds: 60),
            new TrackAnalysisOptions { MaxSpeedKmh = 1000 }
        );
        Assert.Equal(TrackAnomalies.None, check.Anomalies);
        Assert.Equal(90, check.SpeedKmh!.Value, tolerance: 1);
    }

    [Fact]
    public void AMoveThatOnlyChangesAmbiguityIsAnAmbiguityJump()
    {
        var exact = new AprsPosition(39.5580, -104.7942);
        var blurred = new AprsPosition(39.55, -104.7833, Ambiguity: 2); // 39°33′N 104°47′W, to the minute.
        var check = TrackAnalysis.Check([R(exact, 0)], R(blurred, 60));
        Assert.Equal(TrackAnomalies.AmbiguityJump, check.Anomalies);
        Assert.Equal(0, check.DistanceKm);
        Assert.Equal(TrackAnomalies.AmbiguityJump, TrackAnalysis.Check([R(blurred, 0)], R(exact, 60)).Anomalies);
    }

    [Fact]
    public void AMoveOutsideTheAmbiguityAreaIsAMove()
    {
        var blurred = new AprsPosition(39.55, -104.7833, Ambiguity: 2);
        var check = TrackAnalysis.Check([R(blurred, 0)], R(new AprsPosition(39.60, -104.70), 600));
        Assert.Equal(TrackAnomalies.None, check.Anomalies);
        Assert.True(check.DistanceKm > 0);
    }

    [Fact]
    public void NeighbouringAmbiguityAreasAreNotAFastJump()
    {
        // Ambiguity 4 hides everything below a degree: the station may have moved only metres across the boundary.
        var check = TrackAnalysis.Check(
            [R(new AprsPosition(39, -104, Ambiguity: 4), 0)],
            R(new AprsPosition(40, -104, Ambiguity: 4), 60),
            new TrackAnalysisOptions { MaxSpeedKmh = 1000 }
        );
        Assert.Equal(TrackAnomalies.None, check.Anomalies);
        Assert.Equal(0, check.DistanceKm);
    }

    [Fact]
    public void AReportComesFromAnyPositionedPacket()
    {
        var received = new DateTimeOffset(2026, 10, 12, 23, 0, 0, TimeSpan.Zero);
        var timed = TrackReport.FromPacket(P("N0CALL>APRS:@122256z3933.40N/10522.41W>"), received);
        Assert.Equal(new AprsPosition(39.556667, -105.373500), Round(timed!.Value.Position));
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 22, 56, 0, TimeSpan.Zero), timed.Value.ReportedAt);
        Assert.Equal(received, timed.Value.ReceivedAt);

        Assert.Null(TrackReport.FromPacket(P("N0CALL>APRS:!3933.40N/10522.41W>"), received)!.Value.ReportedAt);
        Assert.NotNull(TrackReport.FromPacket(P("N0CALL>APRS:;LEADER   *092345z4903.50N/07201.75W>"), received));
        Assert.NotNull(TrackReport.FromPacket(P(@"QA0ACP>S9UTVY,qAO,Q0AAJ-1:`q[+mRQj/]""EV}146.520="), received));
        Assert.Null(TrackReport.FromPacket(P("N0CALL>APRS:>status"), received));
        Assert.Null(TrackReport.FromPacket(P(@"N0CALL>APRS:!0000.00N\00000.00W."), received));
    }

    [Fact]
    public void CorpusADigipeatedCopyComesBackNineMinutesLate()
    {
        // From 2026-10-12-denver-r50km.log: the 00:40:19 report reappears at 00:49:52 through another digipeater,
        // after the station had moved on.
        var check = Replay(
            ("00:40:19.272", @"QA0ACP>S9UTVY,WIDE1-1,WIDE2-2,qAO,Q0AAJ-1:`q[+mRQj/]""EV}146.520="),
            ("00:48:33.967", @"QA0ACP>S9UTWS,WIDE1-1,WIDE2-2,qAO,Q0AAJ-1:`q[$lJRj/]""EY}146.520="),
            ("00:49:35.509", @"QA0ACP>S9UTVY,WIDE1-1,WIDE2-2,qAO,Q0AAJ-1:`q[&lqDj/]""EZ}146.520="),
            ("00:49:52.406", @"QA0ACP>S9UTVY,Q0AAM,WIDE1,XAAE*,WIDE2-1,qAR,Q0ABK-10:`q[+mRQj/]""EV}146.520=")
        );
        Assert.Equal(TrackAnomalies.RepeatedPosition, check.Anomalies);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 0, 40, 19, 272, TimeSpan.Zero), check.RepeatOf!.Value.ReceivedAt);
    }

    [Fact]
    public void CorpusTwoDevicesShareACallsign()
    {
        // From 2026-10-12-denver-r50km.log: a Mic-E tracker and APRS Voyager in one place both send as QA0BI-9,
        // 0.48 s apart.
        var check = Replay(
            ("00:17:06.936", "QA0BI-9>S9RSVQ,WIDE1-1,WIDE2-1,qAO,Q0AAJ-1:`pDM\u001c\u001c #/\"J$}ARESDEC CommTrailer"),
            (
                "00:17:07.413",
                "QA0BI-9>APMI0,WIDE1-1,WIDE2-1,qAO,Q0AAJ-1:/130017z3923.61N/10440.49W#APRS Voyager U=13.3V."
            )
        );
        Assert.True(check.Anomalies.HasFlag(TrackAnomalies.RapidRepeat));
    }

    [Fact]
    public void CorpusADigipeaterAddsASpace()
    {
        // From 2026-10-12-la-r50km.log: not a duplicate by the APRS-IS rule, but the same report.
        var check = Replay(
            (
                "23:30:22.002",
                "XAAAAV-1>APOT21,WIDE1-1,WIDE2-1,qAO,Q6ABK:/233019h/=Adp0`Ic>a*G/A=000456 14.0V HDOP00.7 SATS12"
            ),
            ("23:30:22.783", "XAAAAV-1>APOT21,Q6AE-4*,qAO,QA6ABE:/233019h/=Adp0`Ic>a*G/A=000456 14.0V HDOP00.7 SATS12 ")
        );
        Assert.Equal(TrackAnomalies.None, check.Anomalies);
    }

    private static AprsPacket P(string line) => AprsPacket.Parse(line);

    private static AprsPosition Round(AprsPosition p) => new(Math.Round(p.Latitude, 6), Math.Round(p.Longitude, 6));

    // Feeds corpus lines received on 12 October (13 October after midnight) through the analysis and returns the
    // check for the last one.
    private static TrackCheck Replay(params (string Time, string Line)[] lines)
    {
        List<TrackReport> track = [];
        TrackCheck check = default;
        foreach (var (time, line) in lines)
        {
            var day = time.StartsWith("00:", StringComparison.Ordinal) ? 13 : 12;
            var received =
                new DateTimeOffset(2026, 10, day, 0, 0, 0, TimeSpan.Zero)
                + TimeSpan.Parse(time, CultureInfo.InvariantCulture);
            var report = TrackReport.FromPacket(P(line), received)!.Value;
            check = TrackAnalysis.Check(track, report);
            track.Add(report);
        }

        return check;
    }
}
