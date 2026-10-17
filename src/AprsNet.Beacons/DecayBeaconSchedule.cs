namespace AprsNet.Beacons;

/// <summary>
/// Beacons soon after something changes, then less and less often: the wait doubles after each beacon up to a ceiling,
/// and starts again from the first step when the content changes. For objects, items and new information, which
/// should spread quickly and then stop crowding the channel (APRS 1.2's decay algorithm).
/// </summary>
/// <example>
/// <code>
/// var schedule = new DecayBeaconSchedule(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));
/// // Due now; after each Sent(): 1, 2, 4, 8, 16, 30, 30 … minutes. Changed() starts again from now.
/// </code>
/// </example>
public sealed class DecayBeaconSchedule : BeaconSchedule
{
    private TimeSpan _step;

    /// <summary>Creates a schedule whose first beacon is due now.</summary>
    /// <param name="firstStep">The wait after the first beacon.</param>
    /// <param name="ceiling">The longest wait; once the doubling reaches it, the schedule beacons at this interval.</param>
    /// <param name="timeProvider">The clock; the system clock by default.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="firstStep"/> is not positive, or <paramref name="ceiling"/> is shorter than it.
    /// </exception>
    public DecayBeaconSchedule(TimeSpan firstStep, TimeSpan ceiling, TimeProvider? timeProvider = null)
        : base(timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(firstStep, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceiling, firstStep);

        FirstStep = firstStep;
        Ceiling = ceiling;
        _step = firstStep;
        Start(new BeaconDue(Time.GetUtcNow(), BeaconReason.Start));
    }

    /// <summary>The wait after the first beacon of a sequence.</summary>
    public TimeSpan FirstStep { get; }

    /// <summary>The longest wait.</summary>
    public TimeSpan Ceiling { get; }

    private protected override BeaconDue OnSent(DateTimeOffset now)
    {
        var wait = _step;
        _step = _step >= Ceiling / 2 ? Ceiling : _step * 2;
        return new BeaconDue(now + wait, wait < Ceiling ? BeaconReason.Decay : BeaconReason.Interval);
    }

    private protected override BeaconDue OnChanged(DateTimeOffset now)
    {
        _step = FirstStep;
        return new BeaconDue(now, BeaconReason.Changed);
    }
}
