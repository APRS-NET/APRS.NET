namespace AprsNet.Kiss;

/// <summary>
/// The most frames a KISS client sends for a TNC to transmit: up to <see cref="Burst"/> at once, then one each
/// <see cref="Interval"/>. A sender waits for its turn rather than fails, so a bug or a loop in an app cannot flood a
/// shared radio channel. Every KISS client has one by default (<see cref="Default"/> settings); pass the same instance
/// to several clients to share one limit between them, such as two clients on the same TNC.
/// </summary>
/// <remarks>
/// The defaults, a burst of 3 and then one frame each 10 seconds, allow a message and its retries or a few objects at
/// once while keeping a runaway sender to 6 frames a minute. They are well above what APRS stations normally send; a
/// fixed station beacons every 10 to 30 minutes. <see cref="Off"/> removes the limit, for a TNC on a channel the app
/// has to itself or a test.
/// </remarks>
// The semaphore never creates a wait handle (only WaitAsync is used), so it holds nothing to dispose; a limit is often
// shared between clients, and being disposable would make its owner unclear.
#pragma warning disable CA1001
public sealed class KissTransmitLimit
#pragma warning restore CA1001
{
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private double _tokens;
    private long _updated;

    /// <summary>Creates a limit.</summary>
    /// <param name="burst">How many frames go out at once before <paramref name="interval"/> applies; 3 by default.</param>
    /// <param name="interval">
    /// How long to wait between frames after a burst; 10 seconds by default. <see cref="TimeSpan.Zero"/> turns the
    /// limit off.
    /// </param>
    /// <param name="timeProvider">The clock for the waits; the system clock by default.</param>
    public KissTransmitLimit(int burst = 3, TimeSpan? interval = null, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(burst, 1);
        Interval = interval ?? TimeSpan.FromSeconds(10);
        ArgumentOutOfRangeException.ThrowIfLessThan(Interval, TimeSpan.Zero, nameof(interval));
        Burst = burst;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _tokens = burst;
        _updated = _timeProvider.GetTimestamp();
    }

    /// <summary>A new limit with the default burst and interval.</summary>
    public static KissTransmitLimit Default => new();

    /// <summary>No limit: every frame is sent at once.</summary>
    public static KissTransmitLimit Off { get; } = new(1, TimeSpan.Zero);

    /// <summary>How many frames go out at once before <see cref="Interval"/> applies.</summary>
    public int Burst { get; }

    /// <summary>How long to wait between frames after a burst; <see cref="TimeSpan.Zero"/> when the limit is off.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Whether frames are limited at all.</summary>
    public bool IsOn => Interval > TimeSpan.Zero;

    /// <summary>Waits until a frame may be sent, then counts it. Waiters go in the order they arrived.</summary>
    internal async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        if (!IsOn)
        {
            return;
        }

        await _turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var now = _timeProvider.GetTimestamp();
                _tokens = Math.Min(Burst, _tokens + (_timeProvider.GetElapsedTime(_updated, now) / Interval));
                _updated = now;
                if (_tokens >= 1)
                {
                    _tokens -= 1;
                    return;
                }

                await Task.Delay(Interval * (1 - _tokens), _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _turn.Release();
        }
    }
}
