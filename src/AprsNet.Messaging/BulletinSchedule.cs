namespace AprsNet.Messaging;

/// <summary>
/// When to send one of your own bulletins or announcements again, and when to stop: often at first, then less often,
/// for its lifetime. A bulletin goes "a few times an hour for a few hours", an announcement "less frequently but
/// possibly over a few days" (APRS101 chapters 3 and 14). It does no I/O: the app sends the bulletin (with
/// <see cref="MessagingStation.Send"/>, without an ID) when <see cref="Next"/> comes due, then calls <see cref="Sent"/>.
/// </summary>
/// <remarks>
/// <para>
/// The first send is due at once. After each send the wait doubles, from <see cref="FirstRepeat"/> up to
/// <see cref="LongestWait"/>, as APRS 1.2's decay does for new information. No send is due once
/// <see cref="Lifetime"/> has passed since the schedule started, or since <see cref="Restart"/>.
/// </para>
/// <para>
/// Each wait is timed from when the bulletin was actually sent, so after a gap (transmit off, the connection down) the
/// schedule asks for one send and carries on; it never asks for the ones it missed. The methods are thread-safe.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var schedule = BulletinSchedule.ForBulletin();
/// // Due now; after each Sent(): 1, 2, 4, 8, 16, 20, 20 … minutes, until 4 hours after it started.
/// </code>
/// </example>
public sealed class BulletinSchedule
{
    private readonly Lock _lock = new();
    private DateTimeOffset _startedAt;
    private DateTimeOffset? _next;
    private DateTimeOffset? _lastSent;
    private TimeSpan _step;

    /// <summary>Creates a schedule whose first send is due now.</summary>
    /// <param name="firstRepeat">The wait after the first send; at least <see cref="RetryProfile.MinimumGap"/>.</param>
    /// <param name="longestWait">The longest wait between sends; the doubling stops there.</param>
    /// <param name="lifetime">How long to keep sending, from the start.</param>
    /// <param name="timeProvider">The clock; the system clock by default.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="firstRepeat"/> is shorter than <see cref="RetryProfile.MinimumGap"/> (APRS-IS would drop the copy
    /// as a duplicate), <paramref name="longestWait"/> is shorter than it, or <paramref name="lifetime"/> is not positive.
    /// </exception>
    public BulletinSchedule(
        TimeSpan firstRepeat,
        TimeSpan longestWait,
        TimeSpan lifetime,
        TimeProvider? timeProvider = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(firstRepeat, RetryProfile.MinimumGap);
        ArgumentOutOfRangeException.ThrowIfLessThan(longestWait, firstRepeat);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        FirstRepeat = firstRepeat;
        LongestWait = longestWait;
        Lifetime = lifetime;
        Time = timeProvider ?? TimeProvider.System;
        Begin(Time.GetUtcNow());
    }

    /// <summary>
    /// For a bulletin (<c>BLN0</c>–<c>BLN9</c>, or a group bulletin): sends now, then after 1, 2, 4, 8 and 16 minutes,
    /// then every 20 minutes, for 4 hours.
    /// </summary>
    /// <param name="timeProvider">The clock; the system clock by default.</param>
    public static BulletinSchedule ForBulletin(TimeProvider? timeProvider = null) =>
        new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(20), TimeSpan.FromHours(4), timeProvider);

    /// <summary>
    /// For an announcement (<c>BLNA</c>–<c>BLNZ</c>): sends now, then after 10, 20 and 40 minutes, then every hour, for
    /// 2 days.
    /// </summary>
    /// <param name="timeProvider">The clock; the system clock by default.</param>
    public static BulletinSchedule ForAnnouncement(TimeProvider? timeProvider = null) =>
        new(TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), TimeSpan.FromDays(2), timeProvider);

    /// <summary>The clock the schedule runs on.</summary>
    public TimeProvider Time { get; }

    /// <summary>The wait after the first send.</summary>
    public TimeSpan FirstRepeat { get; }

    /// <summary>The longest wait between sends.</summary>
    public TimeSpan LongestWait { get; }

    /// <summary>How long to keep sending, from <see cref="StartedAt"/>.</summary>
    public TimeSpan Lifetime { get; }

    /// <summary>When the schedule started, or was last restarted.</summary>
    public DateTimeOffset StartedAt
    {
        get
        {
            lock (_lock)
            {
                return _startedAt;
            }
        }
    }

    /// <summary>When the schedule ends: no send is due after this.</summary>
    public DateTimeOffset EndsAt => StartedAt + Lifetime;

    /// <summary>
    /// When the next send is due, or <see langword="null"/> once the schedule has finished. It may be in the past: the
    /// send is overdue and should go now.
    /// </summary>
    public DateTimeOffset? Next
    {
        get
        {
            lock (_lock)
            {
                return Current(Time.GetUtcNow());
            }
        }
    }

    /// <summary>
    /// Whether the schedule has finished: its lifetime has passed, or <see cref="Stop"/> was called.
    /// </summary>
    public bool IsFinished => Next is null;

    /// <summary>Whether a send is due now (or overdue).</summary>
    public bool IsDue => Next is { } next && next <= Time.GetUtcNow();

    /// <summary>
    /// How long until the next send is due; zero once it is due or overdue, <see langword="null"/> once finished.
    /// </summary>
    public TimeSpan? DueIn
    {
        get
        {
            if (Next is not { } next)
            {
                return null;
            }

            var left = next - Time.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>When the bulletin was last sent, or <see langword="null"/> before the first send.</summary>
    public DateTimeOffset? LastSent
    {
        get
        {
            lock (_lock)
            {
                return _lastSent;
            }
        }
    }

    /// <summary>Tells the schedule the bulletin was just sent, and moves <see cref="Next"/> on.</summary>
    public void Sent()
    {
        lock (_lock)
        {
            var now = Time.GetUtcNow();
            _lastSent = now;
            if (Current(now) is null)
            {
                return;
            }

            _next = now + _step;
            _step = _step >= LongestWait / 2 ? LongestWait : _step * 2;
        }
    }

    /// <summary>
    /// Starts again from now, as for a bulletin whose text was edited: a send is due at once, the waits start again from
    /// <see cref="FirstRepeat"/>, and the lifetime runs from now.
    /// </summary>
    public void Restart()
    {
        lock (_lock)
        {
            Begin(Time.GetUtcNow());
        }
    }

    /// <summary>Ends the schedule: no further send is due.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _next = null;
        }
    }

    /// <summary>
    /// The sends still to come if each goes when it is due, from <see cref="Next"/> until the schedule ends, for a UI
    /// that shows them ("12 more sends, the next in 4 minutes").
    /// </summary>
    public IReadOnlyList<DateTimeOffset> Upcoming()
    {
        lock (_lock)
        {
            var sends = new List<DateTimeOffset>();
            var ends = _startedAt + Lifetime;
            var step = _step;
            for (var at = Current(Time.GetUtcNow()); at is { } send && send < ends; )
            {
                sends.Add(send);
                at = send + step;
                step = step >= LongestWait / 2 ? LongestWait : step * 2;
            }

            return sends;
        }
    }

    private void Begin(DateTimeOffset now)
    {
        _startedAt = now;
        _next = now;
        _step = FirstRepeat;
    }

    /// <summary>The next send, or null once stopped or past the lifetime. Called under the lock.</summary>
    private DateTimeOffset? Current(DateTimeOffset now) =>
        _next is { } next && next < _startedAt + Lifetime && now < _startedAt + Lifetime ? next : null;
}
