using AprsNet.Packets;

namespace AprsNet.Beacons;

/// <summary>
/// Beacons a moving station by its speed and turns, as SmartBeaconing does (HamHUD's algorithm, also in Kenwood and
/// Yaesu radios and Dire Wolf): every <see cref="SmartBeaconSettings.SlowRate"/> when stopped or slow, more often as the
/// speed rises up to <see cref="SmartBeaconSettings.FastRate"/>, and at once on a turn sharper than the speed allows
/// (corner pegging).
/// </summary>
/// <remarks>
/// <para>
/// Give it each GPS fix with <see cref="Update(GpsFix)"/>. Between the slow and fast speeds the interval is
/// <c>FastRate × FastSpeed ÷ speed</c>: twice the fast rate at half the fast speed. The interval runs from the last
/// beacon, so speeding up can make a beacon due at once.
/// </para>
/// <para>
/// A turn counts when the course has changed since the last beacon by more than
/// <c>TurnAngle + TurnSlope ÷ speed</c>, so a slow station has to turn further: GPS courses wander at low speed. A turn
/// beacon comes no sooner than <see cref="SmartBeaconSettings.TurnTime"/> after the last beacon, and not at all below 1
/// mph.
/// </para>
/// <para>The first beacon is due at once.</para>
/// </remarks>
/// <example>
/// <code>
/// var schedule = new SmartBeaconSchedule(); // Dire Wolf's defaults for a car
/// // On each GPS fix:
/// schedule.Update(fix);
/// if (schedule.IsDue) { await SendPositionAsync(fix); schedule.Sent(); }
/// </code>
/// </example>
public sealed class SmartBeaconSchedule : BeaconSchedule
{
    /// <summary>Below this speed, in knots (1 mph), there is no corner pegging: the course means little.</summary>
    private const double MinTurnSpeedKnots = 1 / SmartBeaconSettings.MphPerKnot;

    private double? _speed;
    private double? _course;
    private double? _sentCourse;
    private bool _forced = true;

    /// <summary>Creates a schedule whose first beacon is due now.</summary>
    /// <param name="settings">The speeds, rates and turn settings; <see cref="SmartBeaconSettings"/>'s defaults suit a car.</param>
    /// <param name="timeProvider">The clock; the system clock by default.</param>
    /// <exception cref="ArgumentException">A setting is unusable; the message names it.</exception>
    public SmartBeaconSchedule(SmartBeaconSettings? settings = null, TimeProvider? timeProvider = null)
        : base(timeProvider)
    {
        settings ??= new SmartBeaconSettings();
        settings.Validate();
        Settings = settings;
        Start(new BeaconDue(Time.GetUtcNow(), BeaconReason.Start));
    }

    /// <summary>The settings the schedule follows.</summary>
    public SmartBeaconSettings Settings { get; }

    /// <summary>
    /// The interval at <paramref name="speedKnots"/>: <see cref="SmartBeaconSettings.SlowRate"/> below the slow speed,
    /// <see cref="SmartBeaconSettings.FastRate"/> above the fast speed, and in between, inversely to the speed.
    /// </summary>
    public TimeSpan IntervalAt(double speedKnots)
    {
        if (double.IsNaN(speedKnots) || speedKnots < Settings.SlowSpeedKnots)
        {
            return Settings.SlowRate;
        }

        return speedKnots > Settings.FastSpeedKnots
            ? Settings.FastRate
            : Settings.FastRate * (Settings.FastSpeedKnots / speedKnots);
    }

    /// <summary>
    /// The smallest change of course, in degrees, that is a turn at <paramref name="speedKnots"/>; infinity below 1 mph.
    /// </summary>
    public double TurnThresholdAt(double speedKnots) =>
        speedKnots >= MinTurnSpeedKnots
            ? Settings.TurnAngle + (Settings.TurnSlope / speedKnots)
            : double.PositiveInfinity;

    /// <summary>
    /// Tells the schedule the station's latest speed and course, and moves <see cref="BeaconSchedule.Next"/>. A fix
    /// without a position (<see cref="GpsFixMode.NoFix"/>) is ignored, and a speed or course the fix lacks keeps the last
    /// one known; before any speed is known the station counts as stopped.
    /// </summary>
    public void Update(GpsFix fix)
    {
        ArgumentNullException.ThrowIfNull(fix);
        if (fix.Mode == GpsFixMode.NoFix)
        {
            return;
        }

        Reschedule(
            (now, current) =>
            {
                _speed = fix.SpeedKnots ?? _speed;
                _course = fix.Course ?? _course;
                return _forced ? current : Due();
            }
        );
    }

    private protected override BeaconDue OnSent(DateTimeOffset now)
    {
        _forced = false;
        _sentCourse = _course;
        return Due();
    }

    private protected override BeaconDue OnChanged(DateTimeOffset now)
    {
        _forced = true;
        return new BeaconDue(now, BeaconReason.Changed);
    }

    /// <summary>The next beacon after the last one sent, at the latest speed and course. Called under the lock.</summary>
    private BeaconDue Due()
    {
        var last = LastSentCore!.Value;
        var speed = _speed ?? 0;
        var due = new BeaconDue(last + IntervalAt(speed), BeaconReason.Interval);
        if (_course is { } course && _sentCourse is { } sent && Turned(course, sent) > TurnThresholdAt(speed))
        {
            var turn = last + Settings.TurnTime;
            if (turn < due.At)
            {
                due = new BeaconDue(turn, BeaconReason.Turn);
            }
        }

        return due;
    }

    /// <summary>The change of course from <paramref name="from"/> to <paramref name="to"/>: 0 to 180 degrees.</summary>
    private static double Turned(double to, double from)
    {
        var change = Math.Abs(to - from) % 360;
        return change > 180 ? 360 - change : change;
    }
}

/// <summary>
/// Settings for a <see cref="SmartBeaconSchedule"/>; change them with <c>with</c>. Speeds are in knots, as in <see cref="GpsFix"/>; the defaults are
/// Dire Wolf's for a land vehicle (User Guide, "SmartBeaconing"), which are given in mph.
/// </summary>
public sealed record SmartBeaconSettings
{
    /// <summary>Miles per hour in a knot.</summary>
    internal const double MphPerKnot = 1.150779448;

    /// <summary>At or below this speed the station beacons every <see cref="SlowRate"/>: 5 mph by default.</summary>
    public double SlowSpeedKnots { get; init; } = 5 / MphPerKnot;

    /// <summary>The interval when stopped or slow: 30 minutes by default.</summary>
    public TimeSpan SlowRate { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>At or above this speed the station beacons every <see cref="FastRate"/>: 60 mph by default.</summary>
    public double FastSpeedKnots { get; init; } = 60 / MphPerKnot;

    /// <summary>The interval at speed: 90 seconds by default.</summary>
    public TimeSpan FastRate { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>The smallest change of course, in degrees, that is a turn at high speed: 30 by default.</summary>
    public double TurnAngle { get; init; } = 30;

    /// <summary>
    /// How much more a slow station must turn, in degrees × knots: divided by the speed and added to
    /// <see cref="TurnAngle"/>. 221.6 by default, Dire Wolf's 255 degrees × mph.
    /// </summary>
    public double TurnSlope { get; init; } = 255 / MphPerKnot;

    /// <summary>The shortest time from the last beacon to a turn beacon: 10 seconds by default.</summary>
    public TimeSpan TurnTime { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Checks that the settings can be used.</summary>
    /// <exception cref="ArgumentException">A setting is unusable; the message names it.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(SlowSpeedKnots, 0, nameof(SlowSpeedKnots));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(FastSpeedKnots, SlowSpeedKnots, nameof(FastSpeedKnots));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(FastRate, TimeSpan.Zero, nameof(FastRate));
        ArgumentOutOfRangeException.ThrowIfLessThan(SlowRate, FastRate, nameof(SlowRate));
        if (TurnAngle is < 0 or > 180 || double.IsNaN(TurnAngle))
        {
            throw new ArgumentOutOfRangeException(nameof(TurnAngle), TurnAngle, "A turn angle is 0 to 180 degrees.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(TurnSlope, 0, nameof(TurnSlope));
        ArgumentOutOfRangeException.ThrowIfLessThan(TurnTime, TimeSpan.Zero, nameof(TurnTime));
    }
}
