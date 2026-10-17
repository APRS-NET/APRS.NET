namespace AprsNet.Beacons;

/// <summary>
/// Beacons at a fixed interval, for a fixed station or a status. The first beacon comes after a short random wait, so
/// stations that start together (after a power cut, say) don't all beacon at once.
/// </summary>
/// <example>
/// <code>
/// var schedule = new FixedBeaconSchedule(TimeSpan.FromMinutes(30));
/// // schedule.Next: (now + up to 3 min, Start); after schedule.Sent(): (now + 30 min, Interval)
/// </code>
/// </example>
public sealed class FixedBeaconSchedule : BeaconSchedule
{
    /// <summary>The longest random wait before the first beacon by default.</summary>
    public static readonly TimeSpan MaxStartDelay = TimeSpan.FromMinutes(3);

    /// <summary>Creates a schedule that starts now.</summary>
    /// <param name="interval">The time between beacons.</param>
    /// <param name="startDelay">
    /// The longest random wait before the first beacon. By default a tenth of <paramref name="interval"/>, at most
    /// <see cref="MaxStartDelay"/>; <see cref="TimeSpan.Zero"/> beacons at once.
    /// </param>
    /// <param name="timeProvider">The clock; the system clock by default.</param>
    /// <param name="random">The random source for the first wait; <see cref="Random.Shared"/> by default.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="interval"/> is not positive, or <paramref name="startDelay"/> is negative or longer than it.
    /// </exception>
    public FixedBeaconSchedule(
        TimeSpan interval,
        TimeSpan? startDelay = null,
        TimeProvider? timeProvider = null,
        Random? random = null
    )
        : base(timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        var maxDelay = startDelay ?? Min(interval / 10, MaxStartDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDelay, TimeSpan.Zero, nameof(startDelay));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDelay, interval, nameof(startDelay));

        Interval = interval;
        StartDelay = maxDelay;
        var wait = TimeSpan.FromTicks((long)((random ?? Random.Shared).NextDouble() * maxDelay.Ticks));
        Start(new BeaconDue(Time.GetUtcNow() + wait, BeaconReason.Start));
    }

    /// <summary>The time between beacons.</summary>
    public TimeSpan Interval { get; }

    /// <summary>The longest random wait before the first beacon.</summary>
    public TimeSpan StartDelay { get; }

    private protected override BeaconDue OnSent(DateTimeOffset now) => new(now + Interval, BeaconReason.Interval);

    private protected override BeaconDue OnChanged(DateTimeOffset now) => new(now, BeaconReason.Changed);

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
