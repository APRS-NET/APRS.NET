namespace AprsNet.Messaging;

/// <summary>
/// When a message is sent again while no ack has come: the wait before each retry, and how long to wait for an ack
/// after the last one before the message has failed. Each wait is randomised by ±10 % (APRS 1.2, A12-NET-01), but a
/// retry never comes less than 30 seconds after the previous send, since APRS-IS drops a copy inside 30 seconds as a
/// duplicate.
/// </summary>
public sealed class RetryProfile
{
    /// <summary>The shortest time between two sends of a message, APRS-IS's duplicate window.</summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(30);

    /// <summary>Creates a profile.</summary>
    /// <param name="name">A name a UI can show, for example <c>Default</c>.</param>
    /// <param name="retryDelays">The wait before each retry, in order; none sends the message once.</param>
    /// <param name="ackWait">How long to wait for an ack after the last send before the message has failed.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A wait is shorter than <see cref="MinimumGap"/>.</exception>
    public RetryProfile(string name, IEnumerable<TimeSpan> retryDelays, TimeSpan ackWait)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(retryDelays);
        RetryDelays = [.. retryDelays];
        foreach (var delay in RetryDelays)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(delay, MinimumGap, nameof(retryDelays));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(ackWait, MinimumGap);
        Name = name;
        AckWait = ackWait;
    }

    /// <summary>
    /// For RF and APRS-IS alike: sends at 0, then after 30 s, 1, 2, 4 and 8 minutes, and fails 8 minutes after the
    /// last, about 23 minutes in all.
    /// </summary>
    public static RetryProfile Default { get; } =
        new("Default", [Seconds(30), Minutes(1), Minutes(2), Minutes(4), Minutes(8)], Minutes(8));

    /// <summary>
    /// For APRS-IS: the internet leg is reliable, so fewer retries cover the far IGate's RF leg. Sends at 0, then
    /// after 30 s, 2 and 5 minutes, and fails 5 minutes after the last.
    /// </summary>
    public static RetryProfile AprsIs { get; } = new("AprsIs", [Seconds(30), Minutes(2), Minutes(5)], Minutes(5));

    /// <summary>For 300-baud HF: sends at 0, then after 2, 4, 8 and 16 minutes, and fails 16 minutes after the last.</summary>
    public static RetryProfile SlowHf { get; } =
        new("SlowHf", [Minutes(2), Minutes(4), Minutes(8), Minutes(16)], Minutes(16));

    /// <summary>Sends once, and fails if no ack comes within 2 minutes.</summary>
    public static RetryProfile None { get; } = new("None", [], Minutes(2));

    /// <summary>The name a UI can show.</summary>
    public string Name { get; }

    /// <summary>The wait before each retry, in order.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; }

    /// <summary>How long to wait for an ack after the last send before the message has failed.</summary>
    public TimeSpan AckWait { get; }

    /// <summary>How many times a message is sent at most: once, plus a retry for each delay.</summary>
    public int Sends => RetryDelays.Count + 1;

    /// <inheritdoc />
    public override string ToString() => Name;

    private static TimeSpan Seconds(int seconds) => TimeSpan.FromSeconds(seconds);

    private static TimeSpan Minutes(int minutes) => TimeSpan.FromMinutes(minutes);
}
