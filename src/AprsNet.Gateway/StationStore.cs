using AprsNet.Packets;

namespace AprsNet.Gateway;

/// <summary>
/// What is known about each station heard: when it was last heard, and its last position. Positions come from the
/// station's own position reports, Mic-E and positioned weather, not from objects or items it places elsewhere.
/// </summary>
/// <remarks>
/// Kept in memory, at most <see cref="Capacity"/> stations; the station heard least recently is forgotten first.
/// Callsigns compare ignoring case. Safe to use from several threads.
/// </remarks>
public sealed class StationStore
{
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, LinkedListNode<StationState>> _stations = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<StationState> _byLastHeard = new();
    private readonly Lock _lock = new();

    /// <summary>Creates a store that remembers at most <paramref name="capacity"/> stations.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public StationStore(int capacity = 100_000, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The most stations remembered.</summary>
    public int Capacity { get; }

    /// <summary>Stations remembered now.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _stations.Count;
            }
        }
    }

    /// <summary>Records a packet: its source was heard now, and at the position it reports, if any.</summary>
    public void Observe(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Source is not { } source)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        AprsPosition? position = packet.Payload
            is (PositionReport or MicEReport)
                and PositionedPayload { IsPositionUnknown: false } own
            ? own.Position
            : null;
        lock (_lock)
        {
            if (_stations.Remove(source.Value, out var node))
            {
                _byLastHeard.Remove(node);
                var previous = node.Value;
                node.Value = position is null
                    ? previous with
                    {
                        LastHeard = now,
                    }
                    : previous with
                    {
                        LastHeard = now,
                        Position = position,
                        PositionTime = now,
                    };
            }
            else
            {
                node = new LinkedListNode<StationState>(
                    new StationState(source.Value, now, position, position is null ? null : now)
                );
                if (_stations.Count >= Capacity && _byLastHeard.First is { } oldest)
                {
                    _byLastHeard.RemoveFirst();
                    _stations.Remove(oldest.Value.Callsign);
                }
            }

            _byLastHeard.AddLast(node);
            _stations[source.Value] = node;
        }
    }

    /// <summary>What is known about <paramref name="callsign"/> (with its SSID), or <see langword="null"/>.</summary>
    public StationState? Find(string callsign)
    {
        ArgumentNullException.ThrowIfNull(callsign);
        lock (_lock)
        {
            return _stations.TryGetValue(callsign, out var node) ? node.Value : null;
        }
    }

    /// <summary>The last position of <paramref name="callsign"/>, or <see langword="null"/> when none is known.</summary>
    public AprsPosition? LastPosition(string callsign) => Find(callsign)?.Position;
}

/// <summary>A station as last heard.</summary>
/// <param name="Callsign">The callsign with its SSID, as first heard.</param>
/// <param name="LastHeard">When any packet from it was last seen.</param>
/// <param name="Position">Its last reported position, or <see langword="null"/> if it has not sent one.</param>
/// <param name="PositionTime">When <paramref name="Position"/> was received.</param>
public sealed record StationState(
    string Callsign,
    DateTimeOffset LastHeard,
    AprsPosition? Position,
    DateTimeOffset? PositionTime
);
