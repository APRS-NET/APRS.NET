namespace AprsNet.AprsIsTcpClient;

/// <summary>
/// A token bucket: up to <c>burst</c> packets go out at once, then one more each <c>interval</c>. Callers wait for a
/// token rather than fail, so a burst of beacons or objects is spread out instead of flooding APRS-IS.
/// </summary>
internal sealed class SendRateLimiter(int burst, TimeSpan interval, TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private double _tokens = burst;
    private long _updated = timeProvider.GetTimestamp();

    /// <summary>Waits until a packet may be sent, then takes its token.</summary>
    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        if (interval == TimeSpan.Zero)
        {
            return;
        }

        // One waiter at a time keeps senders in order and the bucket consistent.
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var now = timeProvider.GetTimestamp();
                _tokens = Math.Min(burst, _tokens + (timeProvider.GetElapsedTime(_updated, now) / interval));
                _updated = now;
                if (_tokens >= 1)
                {
                    _tokens -= 1;
                    return;
                }

                await Task.Delay(interval * (1 - _tokens), timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();
}
