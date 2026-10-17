namespace AprsNet.Messaging;

/// <summary>Settings for a <see cref="MessagingStation"/>.</summary>
public sealed class MessagingStationOptions
{
    /// <summary>The retry schedule for messages sent without one of their own. <see cref="RetryProfile.Default"/> by default.</summary>
    public RetryProfile RetryProfile { get; set; } = RetryProfile.Default;

    /// <summary>
    /// The last message ID the station used, saved from <see cref="MessagingStation.LastMessageId"/> before a restart,
    /// so IDs carry on from it rather than repeat. <see langword="null"/>, the default, starts at <c>01</c>.
    /// </summary>
    public string? LastMessageId { get; set; }

    /// <summary>
    /// How long a failed message waits for its correspondent to be heard again, and then gets one more try (APRS 1.2,
    /// "retry on heard"). An hour by default; <see cref="TimeSpan.Zero"/> turns it off.
    /// </summary>
    public TimeSpan RetryOnHeardWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How many times each ack is sent, 30 seconds apart. 1, the default, sends it once; 2 is common on RF, where an ack
    /// is easily lost.
    /// </summary>
    public int AckRepeats { get; set; } = 1;

    /// <summary>
    /// How long a received message ID is remembered, so later copies of it are acked but not delivered again. An hour
    /// by default.
    /// </summary>
    public TimeSpan ReceiveWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The clock for retries. The system clock by default; tests can pass a fake one.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Checks that the settings can be used.</summary>
    /// <exception cref="ArgumentException">A setting is unusable; the message names it.</exception>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(RetryProfile, nameof(RetryProfile));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
        if (LastMessageId is not null && !MessageIds.IsValid(LastMessageId))
        {
            throw new ArgumentException(
                "The last message ID must be two letters or digits, as MessagingStation.LastMessageId gives.",
                nameof(LastMessageId)
            );
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(RetryOnHeardWindow, TimeSpan.Zero, nameof(RetryOnHeardWindow));
        ArgumentOutOfRangeException.ThrowIfLessThan(AckRepeats, 1, nameof(AckRepeats));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ReceiveWindow, TimeSpan.Zero, nameof(ReceiveWindow));
    }
}
