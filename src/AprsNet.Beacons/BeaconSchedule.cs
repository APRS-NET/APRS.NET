namespace AprsNet.Beacons;

/// <summary>Why a beacon is due.</summary>
public enum BeaconReason
{
    /// <summary>The first beacon after the schedule started.</summary>
    Start,

    /// <summary>The schedule's interval has passed since the last beacon.</summary>
    Interval,

    /// <summary>The next step of a decay schedule, before it reaches its ceiling.</summary>
    Decay,

    /// <summary>The content changed, so it should go out now.</summary>
    Changed,

    /// <summary>
    /// The station has gone too far from where its last beacon makes others assume it is (<see cref="DeadReckoning"/>).
    /// </summary>
    Drift,
}

/// <summary>When the next beacon is due, and why.</summary>
/// <param name="At">When it is due. It may be in the past: the beacon is overdue and should go out now.</param>
/// <param name="Reason">Why it is due.</param>
public readonly record struct BeaconDue(DateTimeOffset At, BeaconReason Reason);

/// <summary>
/// Answers when a station should beacon next, and why. It does no I/O: the app sends the beacon when
/// <see cref="Next"/> comes due, then calls <see cref="Sent"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each beacon is timed from when the last one was actually sent, not from when it was due. So after a gap (transmit
/// turned off, the connection down, the app closed) the schedule asks for one beacon now and then carries on; it never
/// asks for the beacons it missed. A manual send ("send position now") is a <see cref="Sent"/> like any other, and the
/// schedule carries on from it.
/// </para>
/// <para>The methods are thread-safe.</para>
/// </remarks>
public abstract class BeaconSchedule
{
    private readonly Lock _lock = new();
    private BeaconDue _next;

    private protected BeaconSchedule(TimeProvider? timeProvider)
    {
        Time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The clock the schedule runs on.</summary>
    public TimeProvider Time { get; }

    /// <summary>When the next beacon is due, and why.</summary>
    public BeaconDue Next
    {
        get
        {
            lock (_lock)
            {
                return _next;
            }
        }
    }

    /// <summary>How long until the next beacon is due; zero once it is due or overdue.</summary>
    public TimeSpan DueIn
    {
        get
        {
            var left = Next.At - Time.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>Whether the next beacon is due now (or overdue).</summary>
    public bool IsDue => DueIn == TimeSpan.Zero;

    /// <summary>When the last beacon was sent, or <see langword="null"/> before the first.</summary>
    public DateTimeOffset? LastSent
    {
        get
        {
            lock (_lock)
            {
                return LastSentCore;
            }
        }
    }

    private protected DateTimeOffset? LastSentCore { get; private set; }

    /// <summary>
    /// Tells the schedule a beacon was just sent, whether it was due or sent by hand, and moves <see cref="Next"/> on.
    /// </summary>
    public void Sent()
    {
        lock (_lock)
        {
            var now = Time.GetUtcNow();
            LastSentCore = now;
            _next = OnSent(now);
        }
    }

    /// <summary>
    /// Tells the schedule the content changed, so it asks for a beacon now (<see cref="BeaconReason.Changed"/>). A decay
    /// schedule also starts its sequence again.
    /// </summary>
    public void Changed()
    {
        lock (_lock)
        {
            var now = Time.GetUtcNow();
            _next = OnChanged(now);
        }
    }

    /// <summary>
    /// Tells the schedule the station has drifted too far from where its last beacon makes others assume it is, so it
    /// asks for a beacon now (<see cref="BeaconReason.Drift"/>). Otherwise the same as <see cref="Changed"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// if (lastBeacon.ErrorAt(gpsFix, now).DistanceKm > 0.5)
    /// {
    ///     schedule.Drifted();
    /// }
    /// </code>
    /// </example>
    public void Drifted()
    {
        lock (_lock)
        {
            var now = Time.GetUtcNow();
            _next = OnChanged(now) with { Reason = BeaconReason.Drift };
        }
    }

    /// <summary>Sets the first <see cref="Next"/>; derived constructors call it once their settings are in place.</summary>
    private protected void Start(BeaconDue first)
    {
        lock (_lock)
        {
            _next = first;
        }
    }

    /// <summary>The next beacon after one sent at <paramref name="now"/>. Called under the lock.</summary>
    private protected abstract BeaconDue OnSent(DateTimeOffset now);

    /// <summary>The next beacon after the content changed at <paramref name="now"/>. Called under the lock.</summary>
    private protected abstract BeaconDue OnChanged(DateTimeOffset now);
}
