using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.IGate;

/// <summary>
/// The stations heard on RF: when each was last heard, over how many digipeater hops and on which radio port, so an
/// IGate can tell which stations are local before it sends them anything from APRS-IS. Safe to use from several
/// threads.
/// </summary>
/// <remarks>
/// <para>
/// Record every packet the TNC hears. A station is local when it was heard within
/// <see cref="HeardStationsOptions.LocalWindow"/> over at most <see cref="HeardStationsOptions.MaxLocalHops"/>, as
/// Dire Wolf's IGate decides: 180 minutes and the hops of the path it transmits with.
/// </para>
/// <para>
/// One transmission is often heard several times, first direct and then through each digipeater. A copy over more hops
/// heard within 30 seconds of a shorter one doesn't replace it, so a station heard direct stays direct.
/// </para>
/// <para>
/// The station recorded is the one that transmitted: for third-party traffic, the station that relayed it on RF, not
/// the one inside. Packets that weren't heard on RF are ignored: any with a q-construct or <c>TCPIP</c> or
/// <c>TCPXX</c> in the path, which came from APRS-IS. At most <see cref="HeardStationsOptions.Capacity"/> stations are
/// kept; when full, the one heard longest ago makes room.
/// </para>
/// </remarks>
public sealed class HeardStations
{
    // A copy over more hops within this long is the same transmission repeated by a digipeater.
    private static readonly TimeSpan CopyWindow = DuplicateFilter.DefaultWindow;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, LinkedListNode<HeardStation>> _byCall = new(StringComparer.OrdinalIgnoreCase);

    // Most recently heard first, so the last one is the next to make room.
    private readonly LinkedList<HeardStation> _byTime = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates an empty list.</summary>
    /// <param name="options">What counts as local, how many stations to keep, and the clock.</param>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public HeardStations(HeardStationsOptions? options = null)
    {
        options ??= new HeardStationsOptions();
        options.Validate();
        LocalWindow = options.LocalWindow;
        MaxLocalHops = options.MaxLocalHops;
        Capacity = options.Capacity;
        _timeProvider = options.TimeProvider;
    }

    /// <summary>How recently a station must have been heard to count as local.</summary>
    public TimeSpan LocalWindow { get; }

    /// <summary>The most digipeater hops a station may have been heard over to count as local.</summary>
    public int MaxLocalHops { get; }

    /// <summary>The most stations kept.</summary>
    public int Capacity { get; }

    /// <summary>How many stations are kept now.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _byCall.Count;
            }
        }
    }

    /// <summary>
    /// Every station kept, most recently heard first: a copy, which later packets don't change.
    /// </summary>
    public IReadOnlyList<HeardStation> Stations
    {
        get
        {
            lock (_lock)
            {
                return [.. _byTime];
            }
        }
    }

    /// <summary>Records a frame from the TNC's only radio port, as <see cref="Record(Ax25Frame, int)"/>.</summary>
    public HeardStation? Record(Ax25Frame frame) => Record(frame, 0);

    /// <summary>Records a packet heard on the TNC's only radio port, as <see cref="Record(AprsPacket, int)"/>.</summary>
    public HeardStation? Record(AprsPacket heard) => Record(heard, 0);

    /// <summary>
    /// Records a frame from the TNC. Frames that aren't APRS (<see cref="Ax25Frame.IsAprs"/>) are ignored, as they are
    /// by <see cref="RfToInternetGate"/>.
    /// </summary>
    /// <param name="frame">The frame as the TNC heard it.</param>
    /// <param name="port">The TNC's radio port it was heard on.</param>
    /// <returns>The station as now recorded, or <see langword="null"/> when the frame was ignored.</returns>
    public HeardStation? Record(Ax25Frame frame, int port)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.IsAprs ? Record(frame.ToAprsPacket(), port) : null;
    }

    /// <summary>Records a packet heard on RF.</summary>
    /// <param name="heard">The packet as the TNC heard it.</param>
    /// <param name="port">The TNC's radio port it was heard on.</param>
    /// <returns>
    /// The station as now recorded, or <see langword="null"/> when the packet was ignored: its header can't be read, or
    /// its path says it came from APRS-IS.
    /// </returns>
    public HeardStation? Record(AprsPacket heard, int port)
    {
        ArgumentNullException.ThrowIfNull(heard);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        if (heard.Source is not { } source || heard.Path is null)
        {
            return null;
        }

        var path = PathAnalysis.FromPacket(heard);
        if (path.QConstruct is not null || path.IsFromInternet)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (_byCall.TryGetValue(source.Value, out var node))
            {
                var last = node.Value;
                var station =
                    path.HopCount > last.Hops && now - last.LastHeard < CopyWindow
                        ? last
                        : last with
                        {
                            LastHeard = now,
                            Hops = path.HopCount,
                            Port = port,
                            Packets = last.Packets + 1,
                        };
                node.Value = station;
                _byTime.Remove(node);
                _byTime.AddFirst(node);
                return station;
            }

            if (_byCall.Count >= Capacity && _byTime.Last is { } oldest)
            {
                _byTime.RemoveLast();
                _byCall.Remove(oldest.Value.Station.Value);
            }

            var added = new HeardStation(source, now, path.HopCount, port) { FirstHeard = now, Packets = 1 };
            _byCall[source.Value] = _byTime.AddFirst(added);
            return added;
        }
    }

    /// <summary>The station <paramref name="station"/> as last recorded, or <see langword="null"/> if it isn't kept.</summary>
    public HeardStation? Find(Callsign station)
    {
        ArgumentNullException.ThrowIfNull(station);
        lock (_lock)
        {
            return _byCall.TryGetValue(station.Value, out var node) ? node.Value : null;
        }
    }

    /// <summary>
    /// Whether <paramref name="station"/> is local: heard on RF within <see cref="LocalWindow"/>, over at most
    /// <see cref="MaxLocalHops"/> digipeater hops.
    /// </summary>
    public bool IsLocal(Callsign station) =>
        Find(station) is { } heard
        && _timeProvider.GetUtcNow() - heard.LastHeard <= LocalWindow
        && heard.Hops <= MaxLocalHops;
}

/// <summary>A station heard on RF, as <see cref="HeardStations"/> last recorded it.</summary>
/// <param name="Station">The station that transmitted.</param>
/// <param name="LastHeard">When it was last heard.</param>
/// <param name="Hops">
/// How many digipeaters repeated what was last heard from it (<see cref="PathAnalysis.HopCount"/>): 0 when heard
/// direct.
/// </param>
/// <param name="Port">The TNC's radio port it was last heard on.</param>
public sealed record HeardStation(Callsign Station, DateTimeOffset LastHeard, int Hops, int Port)
{
    /// <summary>When it was first heard, since it was last added to the list.</summary>
    public DateTimeOffset FirstHeard { get; init; }

    /// <summary>
    /// How many of its transmissions were heard, since it was last added to the list. Copies repeated by digipeaters
    /// within 30 seconds of a shorter one don't count.
    /// </summary>
    public long Packets { get; init; }
}

/// <summary>Settings for <see cref="HeardStations"/>.</summary>
public sealed class HeardStationsOptions
{
    /// <summary>The default for <see cref="LocalWindow"/>: 180 minutes, as Dire Wolf's IGate uses.</summary>
    public static readonly TimeSpan DefaultLocalWindow = TimeSpan.FromMinutes(180);

    /// <summary>
    /// How recently a station must have been heard to count as local. <see cref="DefaultLocalWindow"/> by default.
    /// </summary>
    public TimeSpan LocalWindow { get; set; } = DefaultLocalWindow;

    /// <summary>
    /// The most digipeater hops a station may have been heard over to count as local: 2 by default. Match it to the
    /// path the IGate transmits with, as Dire Wolf does, so a station counts as local only if what is sent can reach it:
    /// 2 for <c>WIDE1-1,WIDE2-1</c>, 0 for no path.
    /// </summary>
    public int MaxLocalHops { get; set; } = 2;

    /// <summary>The most stations kept: 10,000 by default. When full, the one heard longest ago makes room.</summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>The clock for when stations are heard. The system clock by default.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Checks that the settings can be used.</summary>
    /// <exception cref="ArgumentException">A setting is unusable; the message names it.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(LocalWindow, TimeSpan.Zero, nameof(LocalWindow));
        ArgumentOutOfRangeException.ThrowIfNegative(MaxLocalHops, nameof(MaxLocalHops));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Capacity, 0, nameof(Capacity));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
    }
}
