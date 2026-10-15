using System.Text;
using AprsNet.Packets;

namespace AprsNet.Gateway;

/// <summary>
/// Drops copies of a packet heard again within a time window, as APRS-IS servers do: the same source, destination and
/// information field is a duplicate whatever path it took, so a packet heard on RF by several IGates, or from both a
/// TNC and APRS-IS, is passed on once.
/// </summary>
/// <remarks>
/// The window runs from the first copy and is not extended by later ones, so a beacon repeated every 30 seconds is
/// never dropped. Memory is bounded by the number of distinct packets in one window.
/// </remarks>
public sealed class PacketDeduplicator
{
    /// <summary>APRS-IS's duplicate window.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private readonly Queue<(string Key, DateTimeOffset Expires)> _expiry = new();
    private readonly object _lock = new();

    /// <summary>Creates a deduplicator with the given window (30 seconds by default).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="window"/> is not positive.</exception>
    public PacketDeduplicator(TimeSpan? window = null, TimeProvider? timeProvider = null)
    {
        Window = window ?? DefaultWindow;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Window, TimeSpan.Zero, nameof(window));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>How long after its first copy a packet counts as a duplicate.</summary>
    public TimeSpan Window { get; }

    /// <summary>Distinct packets remembered now.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                Expire(_timeProvider.GetUtcNow());
                return _seen.Count;
            }
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> for the first copy of a packet in the window, and remembers it;
    /// <see langword="false"/> for a duplicate. A line with no header is compared as a whole.
    /// </summary>
    public bool TryAccept(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var key = Key(packet);
        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            Expire(now);
            if (!_seen.TryAdd(key, now + Window))
            {
                return false;
            }

            _expiry.Enqueue((key, now + Window));
            return true;
        }
    }

    private void Expire(DateTimeOffset now)
    {
        while (_expiry.TryPeek(out var oldest) && oldest.Expires <= now)
        {
            _expiry.Dequeue();
            _seen.Remove(oldest.Key);
        }
    }

    private static string Key(AprsPacket packet) =>
        packet is { Source: { } source, Destination: { } destination }
            ? $"{source.Value}>{destination.Value}:{Encoding.Latin1.GetString(packet.Information.Span)}"
            : Encoding.Latin1.GetString(packet.Raw.Span);
}
