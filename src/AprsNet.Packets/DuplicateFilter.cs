namespace AprsNet.Packets;

/// <summary>
/// Drops copies of a packet heard again within a time window, as APRS-IS servers do: packets with the same
/// <see cref="PacketIdentity"/> are duplicates whatever path they took. Safe to use from several threads.
/// </summary>
/// <remarks>
/// The window runs from the first copy and is not extended by later ones, so a beacon repeated every 30 seconds is
/// never dropped. At most <see cref="Capacity"/> packets are remembered: in a flood of distinct packets the oldest are
/// forgotten early, so a late copy of one of them may pass, but memory stays bounded.
/// </remarks>
public sealed class DuplicateFilter
{
    /// <summary>APRS-IS's duplicate window: 30 seconds.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The default limit on remembered packets: 100,000, about eight minutes of the full APRS-IS feed, so far more than
    /// one window of it.
    /// </summary>
    public const int DefaultCapacity = 100_000;

    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<PacketIdentity, DuplicateCheck> _seen = [];
    private readonly Queue<(PacketIdentity Identity, DateTimeOffset Expires)> _expiry = new();
    private readonly Lock _lock = new();

    /// <summary>Creates a filter with the given window (30 seconds by default) and capacity.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="window"/> or <paramref name="capacity"/> is not positive.
    /// </exception>
    public DuplicateFilter(TimeSpan? window = null, int capacity = DefaultCapacity, TimeProvider? timeProvider = null)
    {
        Window = window ?? DefaultWindow;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Window, TimeSpan.Zero, nameof(window));
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>How long after its first copy a packet counts as a duplicate.</summary>
    public TimeSpan Window { get; }

    /// <summary>The most packets remembered at once.</summary>
    public int Capacity { get; }

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
    /// <see langword="false"/> for a duplicate.
    /// </summary>
    public bool TryAccept(AprsPacket packet) => !Check(packet).IsDuplicate;

    /// <summary>
    /// Checks <paramref name="packet"/>, heard on <paramref name="source"/>, against the window: the first copy is
    /// remembered with its source and time, and a duplicate is reported with the first copy's.
    /// </summary>
    /// <param name="packet">The packet heard.</param>
    /// <param name="source">Where it was heard, such as <c>aprs-is</c> or <c>tnc1</c>; any name the caller chooses.</param>
    public DuplicateCheck Check(AprsPacket packet, string? source = null)
    {
        var identity = PacketIdentity.Of(packet);
        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            Expire(now);
            if (_seen.TryGetValue(identity, out var first))
            {
                return first with { IsDuplicate = true };
            }

            if (_seen.Count == Capacity)
            {
                _seen.Remove(_expiry.Dequeue().Identity);
            }

            var check = new DuplicateCheck(identity, false, source, now);
            _seen.Add(identity, check);
            _expiry.Enqueue((identity, now + Window));
            return check;
        }
    }

    /// <summary>Whether a copy of <paramref name="packet"/> was accepted within the window, without remembering it.</summary>
    public bool IsDuplicate(AprsPacket packet)
    {
        var identity = PacketIdentity.Of(packet);
        lock (_lock)
        {
            Expire(_timeProvider.GetUtcNow());
            return _seen.ContainsKey(identity);
        }
    }

    private void Expire(DateTimeOffset now)
    {
        while (_expiry.TryPeek(out var oldest) && oldest.Expires <= now)
        {
            _expiry.Dequeue();
            _seen.Remove(oldest.Identity);
        }
    }
}

/// <summary>What <see cref="DuplicateFilter.Check"/> found for a packet.</summary>
/// <param name="Identity">The packet's identity.</param>
/// <param name="IsDuplicate">Whether a copy was already seen within the window.</param>
/// <param name="FirstSource">Where the first copy in the window was heard, if its source was given.</param>
/// <param name="FirstSeen">When the first copy in the window was heard: now, for a first copy.</param>
public readonly record struct DuplicateCheck(
    PacketIdentity Identity,
    bool IsDuplicate,
    string? FirstSource,
    DateTimeOffset FirstSeen
);
