using AprsNet.Packets;

namespace AprsNet.Gateway;

/// <summary>
/// A <see cref="DuplicateFilter"/> that also counts the duplicates it drops in the gateway's metrics
/// (<c>aprsnet.gateway.packets.duplicates</c>).
/// </summary>
public sealed class PacketDeduplicator
{
    /// <summary>APRS-IS's duplicate window.</summary>
    public static readonly TimeSpan DefaultWindow = DuplicateFilter.DefaultWindow;

    private readonly DuplicateFilter _filter;

    /// <summary>Creates a deduplicator with the given window (30 seconds by default).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="window"/> is not positive.</exception>
    public PacketDeduplicator(TimeSpan? window = null, TimeProvider? timeProvider = null) =>
        _filter = new DuplicateFilter(window, timeProvider: timeProvider);

    /// <inheritdoc cref="DuplicateFilter.Window"/>
    public TimeSpan Window => _filter.Window;

    /// <inheritdoc cref="DuplicateFilter.Count"/>
    public int Count => _filter.Count;

    /// <inheritdoc cref="DuplicateFilter.TryAccept"/>
    public bool TryAccept(AprsPacket packet)
    {
        if (_filter.TryAccept(packet))
        {
            return true;
        }

        GatewayMetrics.Duplicate();
        return false;
    }
}
